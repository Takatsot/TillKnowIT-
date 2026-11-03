import type { ReactNode } from "react";
import styles from "./Layout.module.css";

export function Card({ title, children }: { title?: string; children: ReactNode }) {
  return (
    <section className={styles.card}>
      {title ? <h2 className={styles.cardTitle}>{title}</h2> : null}
      {children}
    </section>
  );
}

/** Messages to people. Errors use role "alert" so they are read out; the rest use "status". */
export function Notice({ kind = "info", children }: { kind?: "info" | "success" | "warning" | "danger"; children: ReactNode }) {
  // "Not switched on for your shop" is a normal state, not a failure, so it is shown as information.
  const shown = kind === "danger" && typeof children === "string" && /not switched on/i.test(children) ? "info" : kind;
  return <div role={shown === "danger" ? "alert" : "status"} className={[styles.notice, styles[shown]].join(" ")}>{children}</div>;
}

export interface Column<T> {
  header: string;
  cell: (row: T) => ReactNode;
  align?: "left" | "right";
}

export function Table<T>({ columns, rows, rowKey, empty }: { columns: readonly Column<T>[]; rows: readonly T[]; rowKey: (row: T) => string; empty?: string }) {
  if (rows.length === 0 && empty) return <EmptyState>{empty}</EmptyState>;
  return (
    <div className={styles.tableWrap}>
      <table className={styles.table}>
        <thead>
          <tr>{columns.map((c) => <th key={c.header} scope="col" className={c.align === "right" ? styles.right : undefined}>{c.header}</th>)}</tr>
        </thead>
        <tbody>
          {rows.map((r) => (
            <tr key={rowKey(r)}>{columns.map((c) => <td key={c.header} className={c.align === "right" ? styles.right : undefined}>{c.cell(r)}</td>)}</tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/** A small status pill. `tone` colours it by meaning; without one it is neutral. */
export function Badge({ children, tone }: { children: ReactNode; tone?: "success" | "warning" | "danger" | "info" }) {
  const shown = tone ?? (typeof children === "string" ? toneOf(children) : undefined);
  return <span className={[styles.badge, shown ? styles[`tone-${shown}`] : ""].filter(Boolean).join(" ")}>{children}</span>;
}

/** Common status words get their colour without every screen choosing one. Anything else stays neutral. */
function toneOf(text: string): "success" | "warning" | "danger" | "info" | undefined {
  const word = text.trim().toLowerCase();
  if (/^(active|approved|paid|connected|sent|verified|done|working|delivered|succeeded|received|open|trading|on)$/.test(word)) return "success";
  if (/^(pending|waiting|waiting for approval|draft|partly paid|in progress|wiping|wipe requested|queued|new)$/.test(word)) return "warning";
  if (/^(blocked|failed|disabled|switched off|refused|cancelled|canceled|expired|suspended|overdue|rejected|off)$/.test(word)) return "danger";
  return undefined;
}

export function Stack({ children }: { children: ReactNode }) {
  return <div className={styles.stack}>{children}</div>;
}

export function Inline({ children }: { children: ReactNode }) {
  return <div className={styles.inline}>{children}</div>;
}

/** A friendly "nothing here yet" panel: a small drawing of an empty tray and the words, instead of a bare line of text. */
export function EmptyState({ children }: { children: ReactNode }) {
  return (
    <div className={styles.empty} role="status">
      <svg width="56" height="56" viewBox="0 0 64 64" aria-hidden="true" className={styles.emptyArt}>
        <path d="M10 36 18 14h28l8 22v14a4 4 0 0 1-4 4H14a4 4 0 0 1-4-4z" fill="var(--color-primary-soft)" stroke="var(--color-border-strong)" strokeWidth="2" strokeLinejoin="round" />
        <path d="M10 36h14a8 8 0 0 0 16 0h14" fill="none" stroke="var(--color-border-strong)" strokeWidth="2" strokeLinejoin="round" />
        <circle cx="46" cy="12" r="3" fill="var(--color-accent)" />
        <circle cx="16" cy="9" r="2" fill="var(--color-accent)" opacity="0.6" />
      </svg>
      <p>{children}</p>
    </div>
  );
}
