import styles from "./Brand.module.css";

/**
 * The TillKnowIT mark: a spaza shop awning over a tick ("know it, done"). On a light background the awning is green with brass scallops; on a dark one it is brass and cream.
 * `tile` sets the mark on a rounded green square, which is how the app icon looks.
 */
export function BrandMark({ size = 36, onDark = false, tile = false }: { size?: number; onDark?: boolean; tile?: boolean }) {
  const bar = tile ? "#c0912f" : onDark ? "#f3ecd8" : "#0f5c40";
  const scallopA = tile ? "#f6e9c8" : onDark ? "#c0912f" : "#c0912f";
  const scallopB = tile ? "#c0912f" : onDark ? "#f3ecd8" : "#0f5c40";
  const tick = tile ? "#fffdf7" : onDark ? "#e4c06a" : "#0f5c40";
  return (
    <svg className={styles.mark} width={size} height={size} viewBox="0 0 64 64" role="img" aria-label="TillKnowIT">
      {tile && <rect width="64" height="64" rx="16" fill="#0f5c40" />}
      <g transform={tile ? "translate(8 8) scale(0.75)" : undefined}>
        <rect x="5" y="7" width="54" height="15" rx="4" fill={bar} />
        <path d="M5 22h13.5a6.75 6.75 0 0 1-13.5 0z" fill={scallopA} />
        <path d="M18.5 22H32a6.75 6.75 0 0 1-13.5 0z" fill={scallopB} />
        <path d="M32 22h13.5A6.75 6.75 0 0 1 32 22z" fill={scallopA} />
        <path d="M45.5 22H59a6.75 6.75 0 0 1-13.5 0z" fill={scallopB} />
        <path d="M17 38.5 28 49.5 47 30" fill="none" stroke={tick} strokeWidth="8" strokeLinecap="round" strokeLinejoin="round" />
      </g>
    </svg>
  );
}

/** The mark with the name beside it: TillKnow, then IT on a brass tile. `inverse` is for dark backgrounds. */
export function BrandLockup({ size = 36, inverse = false }: { size?: number; inverse?: boolean }) {
  return (
    <span className={styles.lockup} style={inverse ? { color: "var(--color-on-ink)" } : { color: "var(--color-ink-2)" }}>
      <BrandMark size={size} onDark={inverse} />
      <span className={styles.name} style={{ fontSize: `${Math.max(1.05, size / 26)}rem` }}>
        TillKnow<b>IT</b>
      </span>
    </span>
  );
}
