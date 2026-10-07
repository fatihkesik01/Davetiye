using System.Net;
using Davetiye.Domain.Modules.Memories;
using Davetiye.Infrastructure.Security;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class MemoryTextPolicyTests
{
    private static string S(params int[] codePoints) => string.Concat(codePoints.Select(char.ConvertFromUtf32));

    public static TheoryData<string> ValidEmoji => new()
    {
        S(0x1F600),
        S(0x1F468, 0x200D, 0x1F469, 0x200D, 0x1F467, 0x200D, 0x1F466),
        S(0x1F3F3, 0xFE0F, 0x200D, 0x1F308),
        S(0x1F1F9, 0x1F1F7),
        S(0x1F44D, 0x1F3FD),
        S('1', 0xFE0F, 0x20E3),
        S(0x2764, 0xFE0F),
        S(0x2764, 0xFE0F, 0x200D, 0x1F525),
        S(0x2B50),
        S(0x00A9, 0xFE0F),
        S(0x2122, 0xFE0F),
        S(0x2194, 0xFE0F),
        // England subdivision flag: black flag + g b e n g + cancel tag.
        S(0x1F3F4, 0xE0067, 0xE0062, 0xE0065, 0xE006E, 0xE0067, 0xE007F)
    };

    public static TheoryData<string> InvalidEmoji => new()
    {
        "", "a", "1", "#", S(0x1F600, 0x1F600), S(0x1F600) + "a", S(0x200D), S(0xFE0F), S(0x1F1F9), S(0x1F1F9, 0x1F1F7, 0x1F1E9, 0x1F1EA),
        S(0xE9), "<",
        // Bare text-presentation symbols need VS16.
        S(0x00A9), S(0x00AE), S(0x2122), S(0x2194), S(0x2764), S(0x2600), S(0x203C),
        // Tag characters outside a valid subdivision-flag shape.
        S(0x1F600, 0xE0067, 0xE007F),
        S(0x1F3F4, 0xE0067, 0xE007F),
        S(0x1F3F4, 0xE0067, 0xE0062, 0xE0065, 0xE006E, 0xE0067),
        S(0x1F3F4, 0xE0067, 0xE0062, 0xE0065, 0xE006E, 0xE0067, 0xE0067, 0xE0067, 0xE0067, 0xE007F),
        S(0x1F3F4, 0xE0041, 0xE0042, 0xE007F)
    };

    [Theory, MemberData(nameof(ValidEmoji))]
    public void Real_single_emoji_are_accepted(string emoji) => Assert.True(MemoryTextPolicy.IsSingleEmoji(emoji));

    [Theory, MemberData(nameof(InvalidEmoji))]
    public void Non_emoji_text_symbols_and_malformed_tags_are_rejected(string value) =>
        Assert.False(MemoryTextPolicy.IsSingleEmoji(value));

    [Theory]
    [InlineData("Calışıyorum, çok güzel bir gece! İyi ki geldiniz (öğüşç).")]
    [InlineData("Harika bir düğün: 100% mutlu, \"teşekkürler\" - Ayşe & Ali; ?!")]
    [InlineData("ç ğ ı ö ş ü İ")]
    public void Turkish_text_and_common_punctuation_pass(string text)
    {
        Assert.True(MemoryTextPolicy.TryNormalize(text, MemoryTextPolicy.Field.Text, out var value, out _));
        Assert.Equal(text.Normalize(System.Text.NormalizationForm.FormC), value);
    }

    [Fact]
    public void Ordinary_emoji_inside_text_and_names_pass()
    {
        var text = "Tebrikler " + S(0x1F389, 0x1F468, 0x200D, 0x1F469, 0x200D, 0x1F467) + " " + S(0x2764, 0xFE0F);
        Assert.True(MemoryTextPolicy.TryNormalize(text, MemoryTextPolicy.Field.Text, out var value, out _));
        Assert.Equal(text, value);
        Assert.True(MemoryTextPolicy.TryNormalize("Ayşe " + S(0x1F60A), MemoryTextPolicy.Field.DisplayName, out _, out _));
    }

    [Theory]
    [InlineData(0x200B)] // zero width space
    [InlineData(0x2060)] // word joiner
    [InlineData(0x00AD)] // soft hyphen
    [InlineData(0x200E)]
    [InlineData(0x202E)]
    [InlineData(0x2066)]
    [InlineData(0xFEFF)]
    [InlineData(0x2028)]
    [InlineData(0xE0067)] // tag character
    [InlineData(0x0007)]
    public void Invisible_format_and_control_characters_are_rejected_in_text_and_names(int codePoint)
    {
        Assert.False(MemoryTextPolicy.TryNormalize("ab" + S(codePoint) + "cd", MemoryTextPolicy.Field.Text, out _, out _));
        Assert.False(MemoryTextPolicy.TryNormalize("ab" + S(codePoint) + "cd", MemoryTextPolicy.Field.DisplayName, out _, out _));
        Assert.False(MemoryTextPolicy.TryNormalize(S(codePoint), MemoryTextPolicy.Field.Text, out _, out _));
        Assert.False(MemoryTextPolicy.TryNormalize(S(codePoint, codePoint), MemoryTextPolicy.Field.DisplayName, out _, out _));
    }

    [Theory]
    [InlineData(0x3164)]
    [InlineData(0x2800)]
    [InlineData(0xFFA0)]
    [InlineData(0x115F)]
    public void Only_blank_looking_fillers_are_not_visible_content(int codePoint) =>
        Assert.False(MemoryTextPolicy.TryNormalize(" " + S(codePoint) + " ", MemoryTextPolicy.Field.Text, out _, out _));

    [Theory]
    [InlineData("...")]
    [InlineData("!?")]
    [InlineData("́́")]
    public void Text_without_a_letter_digit_symbol_or_emoji_is_rejected(string text) =>
        Assert.False(MemoryTextPolicy.TryNormalize(text, MemoryTextPolicy.Field.Text, out _, out _));

    [Fact]
    public void Zwj_and_zwnj_are_allowed_when_visible_content_exists()
    {
        Assert.True(MemoryTextPolicy.TryNormalize("a" + S(0x200C) + "b", MemoryTextPolicy.Field.Text, out _, out _));
        Assert.True(MemoryTextPolicy.TryNormalize(S(0x1F468, 0x200D, 0x1F469), MemoryTextPolicy.Field.Text, out _, out _));
    }

    [Fact]
    public void Newline_runs_and_combining_marks_are_bounded()
    {
        Assert.True(MemoryTextPolicy.TryNormalize("a\n\nb", MemoryTextPolicy.Field.Text, out _, out _));
        Assert.True(MemoryTextPolicy.TryNormalize("a\r\n\r\nb", MemoryTextPolicy.Field.Text, out _, out _));
        Assert.False(MemoryTextPolicy.TryNormalize("a\n\n\nb", MemoryTextPolicy.Field.Text, out _, out _));
        Assert.False(MemoryTextPolicy.TryNormalize("a\r\n\r\n\r\nb", MemoryTextPolicy.Field.Text, out _, out _));

        var eight = "x" + string.Concat(Enumerable.Repeat(S(0x0300), 8));
        var nine = "x" + string.Concat(Enumerable.Repeat(S(0x0300), 9));
        Assert.True(MemoryTextPolicy.TryNormalize(eight, MemoryTextPolicy.Field.Text, out _, out _));
        Assert.False(MemoryTextPolicy.TryNormalize(nine, MemoryTextPolicy.Field.Text, out _, out _));
        Assert.False(MemoryTextPolicy.TryNormalize(nine, MemoryTextPolicy.Field.DisplayName, out _, out _));
    }

    [Fact]
    public void Oversized_input_is_rejected_before_normalization()
    {
        Assert.False(MemoryTextPolicy.TryNormalize(new string('a', 2001), MemoryTextPolicy.Field.Text, out _, out var error));
        Assert.NotNull(error);
        Assert.False(MemoryTextPolicy.TryNormalize(new string('a', 241), MemoryTextPolicy.Field.DisplayName, out _, out _));
        Assert.False(MemoryTextPolicy.TryNormalize(new string('a', 129), MemoryTextPolicy.Field.Emoji, out _, out _));
        Assert.True(MemoryTextPolicy.TryNormalize(new string('a', 500), MemoryTextPolicy.Field.Text, out _, out _));
    }

    [Fact]
    public void Lone_surrogate_is_rejected_and_never_throws()
    {
        Assert.False(MemoryTextPolicy.TryNormalize("bad\uD800", MemoryTextPolicy.Field.Text, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Text_keeps_line_feed_and_normalizes_crlf_but_names_reject_it()
    {
        Assert.True(MemoryTextPolicy.TryNormalize(" a\r\nb ", MemoryTextPolicy.Field.Text, out var text, out _));
        Assert.Equal("a\nb", text);
        Assert.False(MemoryTextPolicy.TryNormalize("a\nb", MemoryTextPolicy.Field.DisplayName, out _, out _));
    }

    [Fact]
    public void Blank_values_become_null()
    {
        Assert.True(MemoryTextPolicy.TryNormalize("   ", MemoryTextPolicy.Field.Text, out var value, out _));
        Assert.Null(value);
    }

    [Theory]
    [InlineData("203.0.113.9", "203.0.113.9")]
    [InlineData("::ffff:203.0.113.9", "203.0.113.9")]
    [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:dddd", "2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:2:1:2:3:4", "2001:db8:1:2::/64")]
    public void Rate_limit_keys_collapse_ipv6_to_a_64_prefix(string address, string expected) =>
        Assert.Equal(expected, SecurityServiceCollectionExtensions.NormalizeClientKey(IPAddress.Parse(address)));

    [Fact]
    public void Missing_remote_address_has_a_stable_key() =>
        Assert.Equal("unknown", SecurityServiceCollectionExtensions.NormalizeClientKey(null));
}
