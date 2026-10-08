import type { ReactNode } from 'react'
import { type AvatarKey, isAvatarKey } from './preferencesContext'

/**
 * Twelve original, fixed-colour avatar tiles (64x64 viewBox). Colours are intentionally not theme
 * tokens so a chosen avatar looks the same in every palette and in light and dark. The SVGs are
 * decorative: the accessible name is always supplied by the surrounding control.
 */
interface Look { bg: string; face: ReactNode }

const smile = (ink: string) => <path d="M24 40c2.5 5 13.5 5 16 0" fill="none" stroke={ink} strokeWidth="3" strokeLinecap="round" />
const dotEyes = (ink: string, y = 30, dx = 9) => <><circle cx={32 - dx} cy={y} r="3" fill={ink} /><circle cx={32 + dx} cy={y} r="3" fill={ink} /></>
const blush = (c: string) => <><ellipse cx="17" cy="38" rx="4" ry="2.6" fill={c} opacity=".6" /><ellipse cx="47" cy="38" rx="4" ry="2.6" fill={c} opacity=".6" /></>

const looks: Record<AvatarKey, Look> = {
  sunny: { bg: '#FFC93C', face: <>
    <path d="M32 8l3 7 6-5-1 8 7-3-4 7H21l-4-7 7 3-1-8 6 5z" fill="#FF8A3C" />
    {dotEyes('#4A2E00', 31)}<path d="M23 39c3 8 15 8 18 0z" fill="#4A2E00" />{blush('#FF8A3C')}</> },
  mint: { bg: '#5FD6B0', face: <>
    <path d="M32 15c-1-6 4-9 9-7-1 5-4 7-9 7z" fill="#1E8F6E" />
    <path d="M22 31q3-4 6 0M36 31q3-4 6 0" fill="none" stroke="#06382B" strokeWidth="3" strokeLinecap="round" />
    <path d="M26 40h12" stroke="#06382B" strokeWidth="3" strokeLinecap="round" />{blush('#F28DA6')}</> },
  berry: { bg: '#C2417C', face: <>
    <circle cx="23" cy="29" r="7" fill="none" stroke="#FFF3F8" strokeWidth="2.5" /><circle cx="41" cy="29" r="7" fill="none" stroke="#FFF3F8" strokeWidth="2.5" /><path d="M30 29h4" stroke="#FFF3F8" strokeWidth="2.5" />
    <circle cx="23" cy="29" r="2.4" fill="#FFF3F8" /><circle cx="41" cy="29" r="2.4" fill="#FFF3F8" />{smile('#FFF3F8')}</> },
  sky: { bg: '#5BB5F0', face: <>
    <path d="M14 23c4-10 32-10 36 0-9-5-27-5-36 0z" fill="#FFFFFF" />
    <circle cx="23" cy="32" r="3.4" fill="#0B2F4A" /><circle cx="41" cy="32" r="3.4" fill="#0B2F4A" /><circle cx="24.2" cy="30.8" r="1.1" fill="#FFFFFF" /><circle cx="42.2" cy="30.8" r="1.1" fill="#FFFFFF" />
    <ellipse cx="32" cy="43" rx="5" ry="4" fill="#0B2F4A" /></> },
  coral: { bg: '#FF6F61', face: <>
    <path d="M25 18c2-5 5-5 7 0 2-5 5-5 7 0" fill="none" stroke="#3A0B06" strokeWidth="3" strokeLinecap="round" />
    <path d="M21 31h8M35 31h8" stroke="#3A0B06" strokeWidth="3.4" strokeLinecap="round" />
    <path d="M24 40c2.5 5 13.5 5 16 0z" fill="#FFFFFF" stroke="#3A0B06" strokeWidth="2.5" strokeLinejoin="round" /></> },
  lilac: { bg: '#B79CF2', face: <>
    <path d="M32 11v9" stroke="#2A1457" strokeWidth="2.5" strokeLinecap="round" /><circle cx="32" cy="10" r="3.5" fill="#FFFFFF" />
    <circle cx="23" cy="31" r="4" fill="#FFFFFF" /><circle cx="41" cy="31" r="4" fill="#FFFFFF" /><circle cx="24" cy="31.4" r="2" fill="#2A1457" /><circle cx="42" cy="31.4" r="2" fill="#2A1457" />
    <circle cx="32" cy="42" r="3" fill="#2A1457" />{blush('#FFFFFF')}</> },
  amber: { bg: '#F59E0B', face: <>
    <path d="M12 25c2-13 38-13 40 0z" fill="#7C2D12" /><path d="M10 25h44" stroke="#7C2D12" strokeWidth="3.5" strokeLinecap="round" />
    {dotEyes('#3B2200', 34, 9)}<path d="M25 43c4 3 10 3 14 0" fill="none" stroke="#3B2200" strokeWidth="3" strokeLinecap="round" /></> },
  forest: { bg: '#2F7D4F', face: <>
    <path d="M32 7l8 12H24z" fill="#8AD3A5" />
    {dotEyes('#EAF9EF', 32, 9)}<path d="M25 41c4 4 10 4 14 0" fill="none" stroke="#EAF9EF" strokeWidth="3" strokeLinecap="round" /><circle cx="46" cy="40" r="1.8" fill="#EAF9EF" /></> },
  night: { bg: '#2B2F77', face: <>
    <path d="M48 11a8 8 0 1 0 5 9 6.5 6.5 0 0 1-5-9z" fill="#FFE9A8" /><circle cx="14" cy="16" r="1.4" fill="#FFE9A8" /><circle cx="23" cy="10" r="1" fill="#FFE9A8" />
    <path d="M20 33q3-4 6 0M38 33q3-4 6 0" fill="none" stroke="#FFE9A8" strokeWidth="3" strokeLinecap="round" />
    <path d="M28 42q4 3 8 0" fill="none" stroke="#FFE9A8" strokeWidth="3" strokeLinecap="round" /></> },
  rose: { bg: '#F27AA5', face: <>
    <circle cx="45" cy="16" r="6" fill="#D6336C" /><circle cx="45" cy="16" r="2.2" fill="#FFD0E0" /><circle cx="38" cy="12" r="3.5" fill="#D6336C" />
    <ellipse cx="23" cy="31" rx="3" ry="4" fill="#4A0D26" /><ellipse cx="41" cy="31" rx="3" ry="4" fill="#4A0D26" />
    {smile('#4A0D26')}{blush('#D6336C')}</> },
  slate: { bg: '#64748B', face: <>
    <rect x="14" y="24" width="16" height="12" rx="4" fill="#F8FAFC" /><rect x="34" y="24" width="16" height="12" rx="4" fill="#F8FAFC" /><path d="M30 29h4" stroke="#F8FAFC" strokeWidth="2.5" />
    <circle cx="22" cy="30.5" r="2.6" fill="#1E293B" /><circle cx="42" cy="30.5" r="2.6" fill="#1E293B" /><path d="M26 44h12" stroke="#F8FAFC" strokeWidth="3" strokeLinecap="round" /></> },
  peach: { bg: '#FFA57A', face: <>
    <path d="M32 16c-7-4-6-10 0-10s7 6 0 10z" fill="#2E8B57" />
    <path d="M21 31c1.5-3 5.5-3 7 0M36 31c1.5-3 5.5-3 7 0" fill="none" stroke="#4A1D0A" strokeWidth="3" strokeLinecap="round" />
    <path d="M25 39c1 7 13 7 14 0z" fill="#4A1D0A" />{blush('#FF6F61')}</> },
}

export function GenericPersonIcon() {
  return <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" focusable="false"><circle cx="12" cy="8.5" r="3.5" /><path d="M5 20c.7-3.6 3.4-5.5 7-5.5s6.3 1.9 7 5.5" /></svg>
}

interface AvatarProps {
  avatarKey: AvatarKey | null
  /** Rendered edge length in CSS pixels. */
  size?: number
  /** When set the image gets this accessible name; otherwise it is decorative. */
  label?: string
  className?: string
}

export function Avatar({ avatarKey, size = 32, label, className }: AvatarProps) {
  const a11y = label ? { role: 'img' as const, 'aria-label': label } : { 'aria-hidden': true as const }
  const extra = className ? ` ${className}` : ''
  if (!isAvatarKey(avatarKey)) {
    return <span className={`avatar avatar--generic${extra}`} data-avatar="none" {...a11y}><GenericPersonIcon /></span>
  }
  return <svg className={`avatar avatar--${avatarKey}${extra}`} data-avatar={avatarKey} width={size} height={size} viewBox="0 0 64 64" focusable="false" {...a11y}>
    <rect width="64" height="64" rx="18" fill={looks[avatarKey].bg} />
    {looks[avatarKey].face}
  </svg>
}
