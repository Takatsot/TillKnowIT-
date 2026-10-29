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
  return <div role={kind === "danger" ? "alert" : "status"} className={[styles.notice, styles[kind]].join(" ")}>{children}</div>;
}

export interface Column<T> {
  header: string;
  cell: (row: T) => ReactNode;
  align?: "left" | "right";
}

export function Table<T>({ columns, rows, rowKey, empty }: { columns: readonly Column<T>[]; rows: readonly T[]; rowKey: (row: T) => string; empty?: string }) {
  if (rows.length === 0 && empty) return <p>{empty}</p>;
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

export function Badge({ children }: { children: ReactNode }) {
  return <span className={styles.badge}>{children}</span>;
}

export function Stack({ children }: { children: ReactNode }) {
  return <div className={styles.stack}>{children}</div>;
}

export function Inline({ children }: { children: ReactNode }) {
  return <div className={styles.inline}>{children}</div>;
}
