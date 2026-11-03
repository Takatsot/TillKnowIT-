import styles from "./Brand.module.css";

/** The TillKnowIT mark: a shop awning over a till, in green and brass. */
export function BrandMark({ size = 36 }: { size?: number }) {
  return (
    <svg className={styles.mark} width={size} height={size} viewBox="0 0 48 48" role="img" aria-label="TillKnowIT">
      <rect width="48" height="48" rx="12" fill="#0f5c40" />
      <rect width="48" height="48" rx="12" fill="url(#tk-sheen)" />
      <path d="M8 18 11 9h26l3 9z" fill="#c0912f" />
      <path d="M8 18h8a4 4 0 0 1-8 0zm8 0h8a4 4 0 0 1-8 0zm8 0h8a4 4 0 0 1-8 0zm8 0h8a4 4 0 0 1-8 0z" fill="#f6e9c8" />
      <rect x="11" y="25" width="26" height="14" rx="3" fill="#fffdf7" />
      <rect x="14.5" y="28.5" width="11" height="3" rx="1.5" fill="#0f5c40" />
      <circle cx="31" cy="30" r="1.6" fill="#c0912f" />
      <circle cx="31" cy="35" r="1.6" fill="#0f5c40" opacity="0.5" />
      <defs>
        <linearGradient id="tk-sheen" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" stopColor="#fff" stopOpacity="0.18" />
          <stop offset="1" stopColor="#fff" stopOpacity="0" />
        </linearGradient>
      </defs>
    </svg>
  );
}

/** The mark with the name beside it. `inverse` is for dark backgrounds. */
export function BrandLockup({ size = 36, inverse = false }: { size?: number; inverse?: boolean }) {
  return (
    <span className={styles.lockup} style={inverse ? { color: "var(--color-on-ink)" } : undefined}>
      <BrandMark size={size} />
      <span className={styles.name}>Till<b>Know</b>IT</span>
    </span>
  );
}
