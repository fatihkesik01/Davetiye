using System.Globalization;
using System.Text;

namespace Davetiye.Domain.Modules.Memories;

/// <summary>
/// Pure guest-text hygiene for Memories. Stored text is inert data (never markup): this policy only
/// normalizes to NFC and rejects control, format (invisible), separator, bidi-control and tag characters,
/// requires visible content, bounds newline runs and combining marks, and validates that an "emoji" is
/// exactly one emoji grapheme. Output encoding is the renderer's job; nothing here sanitizes HTML.
/// </summary>
public static class MemoryTextPolicy
{
    public enum Field { DisplayName, Text, Emoji }

    /// <summary>Longest run of consecutive line feeds accepted in text.</summary>
    public const int MaxConsecutiveNewlines = 2;

    /// <summary>Most combining marks accepted on one grapheme (blocks zalgo text).</summary>
    public const int MaxCombiningMarksPerGrapheme = 8;

    /// <summary>
    /// Cheap pre-normalization length ceilings (hard maximum x 4, since NFC can expand a code point to at most
    /// a few). The database check constraints and configured limits remain the authoritative bounds.
    /// </summary>
    private static int PreNormalizationCeiling(Field field) => field switch
    {
        Field.DisplayName => MemoryInputLimits.HardMaxDisplayNameCharacters * 4,
        Field.Text => MemoryInputLimits.HardMaxTextCharacters * 4,
        _ => MemoryInputLimits.HardMaxEmojiCharacters * 4
    };

    /// <summary>Returns the NFC-normalized, trimmed value (null when blank) or an error message.</summary>
    public static bool TryNormalize(string? input, Field field, out string? value, out string? error)
    {
        value = null;
        error = null;
        if (input is null) return true;
        if (input.Length > PreNormalizationCeiling(field))
        {
            error = "Value is too long.";
            return false;
        }
        string normalized;
        try
        {
            normalized = input.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            error = "Value contains invalid characters.";
            return false;
        }
        if (field == Field.Text) normalized = normalized.Replace("\r\n", "\n", StringComparison.Ordinal);
        foreach (var rune in normalized.EnumerateRunes())
        {
            if (IsForbidden(rune, field))
            {
                error = "Value contains control, invisible or directional-override characters.";
                return false;
            }
        }
        var trimmed = normalized.Trim();
        if (trimmed.Length == 0) return true;

        if (field == Field.Emoji)
        {
            if (!IsSingleEmoji(trimmed))
            {
                error = "Emoji must be a single emoji.";
                return false;
            }
            value = trimmed;
            return true;
        }

        if (!trimmed.EnumerateRunes().Any(IsVisible))
        {
            error = "Value must contain at least one letter, digit, symbol or emoji.";
            return false;
        }
        if (field == Field.Text && HasNewlineRunLongerThan(trimmed, MaxConsecutiveNewlines))
        {
            error = $"Text may not contain more than {MaxConsecutiveNewlines} consecutive line breaks.";
            return false;
        }
        if (HasExcessCombiningMarks(trimmed))
        {
            error = "Value contains too many combining marks.";
            return false;
        }
        value = trimmed;
        return true;
    }

    private static bool IsForbidden(Rune rune, Field field)
    {
        var v = rune.Value;
        if (v == '\n') return field != Field.Text;
        if (v is 0x2028 or 0x2029) return true;
        var category = Rune.GetUnicodeCategory(rune);
        if (category == UnicodeCategory.Format)
        {
            // ZWJ/ZWNJ are needed by emoji sequences and by Persian/Turkish-adjacent scripts. Tag characters are
            // allowed only inside the Emoji field (subdivision flags). Every other format character (bidi controls,
            // zero-width space, word joiner, soft hyphen, BOM, ...) is invisible and rejected.
            if (v is 0x200C or 0x200D) return false;
            if (v is >= 0xE0020 and <= 0xE007F) return field != Field.Emoji;
            return true;
        }
        return category is UnicodeCategory.Control or UnicodeCategory.OtherNotAssigned
            or UnicodeCategory.PrivateUse or UnicodeCategory.Surrogate;
    }

    /// <summary>A letter, digit or symbol that renders visibly (blank-looking fillers are excluded).</summary>
    private static bool IsVisible(Rune rune)
    {
        var v = rune.Value;
        if (v is 0x115F or 0x1160 or 0x3164 or 0xFFA0 or 0x17B4 or 0x17B5 or 0x2800) return false;
        return Rune.GetUnicodeCategory(rune) is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter
            or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber
            or UnicodeCategory.MathSymbol or UnicodeCategory.CurrencySymbol or UnicodeCategory.ModifierSymbol
            or UnicodeCategory.OtherSymbol;
    }

    private static bool HasNewlineRunLongerThan(string value, int maximum)
    {
        var run = 0;
        foreach (var character in value)
        {
            run = character == '\n' ? run + 1 : 0;
            if (run > maximum) return true;
        }
        return false;
    }

    private static bool HasExcessCombiningMarks(string value)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(value);
        while (enumerator.MoveNext())
        {
            var marks = 0;
            foreach (var rune in ((string)enumerator.Current).EnumerateRunes())
            {
                if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
                    or UnicodeCategory.EnclosingMark && ++marks > MaxCombiningMarksPerGrapheme)
                    return true;
            }
        }
        return false;
    }

    /// <summary>Exactly one extended grapheme cluster built only from emoji, joiner, modifier, tag and keycap parts.</summary>
    public static bool IsSingleEmoji(string value)
    {
        if (string.IsNullOrEmpty(value) || new StringInfo(value).LengthInTextElements != 1) return false;
        var runes = value.EnumerateRunes().ToArray();
        if (runes.Any(rune => rune.Value is >= 0xE0020 and <= 0xE007F) && !IsSubdivisionFlag(runes)) return false;
        var pictographic = 0;
        var regionalIndicators = 0;
        var keycap = runes.Length >= 2 && runes[^1].Value == 0x20E3;
        for (var i = 0; i < runes.Length; i++)
        {
            var v = runes[i].Value;
            if (v is >= 0x1F1E6 and <= 0x1F1FF) { regionalIndicators++; continue; }
            if (IsPictographic(v))
            {
                // Text-presentation symbols (copyright, trade mark, arrows, ...) are only emoji with VS16.
                if (!HasDefaultEmojiPresentation(v) && !(i + 1 < runes.Length && runes[i + 1].Value == 0xFE0F)) return false;
                pictographic++;
                continue;
            }
            if (v is 0x200D or 0xFE0F or 0x20E3 or (>= 0x1F3FB and <= 0x1F3FF) or (>= 0xE0020 and <= 0xE007F)) continue;
            if (keycap && v is (>= '0' and <= '9') or '#' or '*') { pictographic++; continue; }
            return false;
        }
        if (regionalIndicators is not (0 or 2)) return false;
        return pictographic + (regionalIndicators == 2 ? 1 : 0) > 0;
    }

    /// <summary>Black flag + 2 to 6 lowercase-letter/digit tags + cancel tag, nothing else.</summary>
    private static bool IsSubdivisionFlag(Rune[] runes)
    {
        if (runes.Length < 4 || runes.Length > 9 || runes[0].Value != 0x1F3F4 || runes[^1].Value != 0xE007F) return false;
        var tags = runes[1..^1];
        return tags.Length is >= 2 and <= 6 &&
               tags.All(tag => tag.Value is (>= 0xE0061 and <= 0xE007A) or (>= 0xE0030 and <= 0xE0039));
    }

    private static bool IsPictographic(int v) =>
        v is 0x00A9 or 0x00AE or 0x203C or 0x2049 or 0x2122 or 0x2139 or 0x24C2 or 0x3030 or 0x303D or 0x3297 or 0x3299
            or (>= 0x2194 and <= 0x21AA) or (>= 0x231A and <= 0x23FF) or (>= 0x25AA and <= 0x25FE)
            or (>= 0x2600 and <= 0x27BF) or (>= 0x2934 and <= 0x2935) or (>= 0x2B05 and <= 0x2B55)
            or (>= 0x1F000 and <= 0x1F0FF) or (>= 0x1F170 and <= 0x1F251) or (>= 0x1F300 and <= 0x1FAFF);

    /// <summary>
    /// Emoji_Presentation=Yes below U+1F300 (these render as emoji without VS16). Code points from U+1F300 are
    /// treated leniently as emoji-presentation; a few rare ones there technically want VS16.
    /// </summary>
    private static bool HasDefaultEmojiPresentation(int v) =>
        v >= 0x1F300 ||
        v is 0x231A or 0x231B or (>= 0x23E9 and <= 0x23EC) or 0x23F0 or 0x23F3 or 0x25FD or 0x25FE
            or 0x2614 or 0x2615 or (>= 0x2648 and <= 0x2653) or 0x267F or 0x2693 or 0x26A1 or 0x26AA or 0x26AB
            or 0x26BD or 0x26BE or 0x26C4 or 0x26C5 or 0x26CE or 0x26D4 or 0x26EA or 0x26F2 or 0x26F3 or 0x26F5
            or 0x26FA or 0x26FD or 0x2705 or 0x270A or 0x270B or 0x2728 or 0x274C or 0x274E
            or (>= 0x2753 and <= 0x2755) or 0x2757 or (>= 0x2795 and <= 0x2797) or 0x27B0 or 0x27BF
            or 0x2B1B or 0x2B1C or 0x2B50 or 0x2B55
            or 0x1F004 or 0x1F0CF or 0x1F18E or (>= 0x1F191 and <= 0x1F19A) or 0x1F201 or 0x1F21A or 0x1F22F
            or (>= 0x1F232 and <= 0x1F236) or (>= 0x1F238 and <= 0x1F23A) or 0x1F250 or 0x1F251;
}
