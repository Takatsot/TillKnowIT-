import { useId, type InputHTMLAttributes, type ReactNode, type SelectHTMLAttributes, type TextareaHTMLAttributes } from "react";
import styles from "./Form.module.css";

interface FieldProps {
  label: string;
  hint?: string;
  error?: string | null;
  children: (props: { id: string; "aria-invalid": boolean; "aria-describedby": string | undefined; className: string }) => ReactNode;
}

/** A labelled control with an optional hint and error, wired so screen readers read both. */
export function Field({ label, hint, error, children }: FieldProps) {
  const id = useId();
  const note = error ? `${id}-error` : hint ? `${id}-hint` : undefined;
  return (
    <div className={styles.field}>
      <label htmlFor={id} className={styles.label}>{label}</label>
      {children({ id, "aria-invalid": Boolean(error), "aria-describedby": note, className: styles.control! })}
      {error ? <span id={`${id}-error`} role="alert" className={styles.error}>{error}</span> : hint ? <span id={`${id}-hint`} className={styles.hint}>{hint}</span> : null}
    </div>
  );
}

type Shared = { label: string; hint?: string; error?: string | null };

export function TextField({ label, hint, error, ...rest }: Shared & InputHTMLAttributes<HTMLInputElement>) {
  return <Field label={label} hint={hint} error={error}>{(p) => <input {...p} {...rest} />}</Field>;
}

export function TextAreaField({ label, hint, error, ...rest }: Shared & TextareaHTMLAttributes<HTMLTextAreaElement>) {
  return <Field label={label} hint={hint} error={error}>{(p) => <textarea {...p} rows={3} {...rest} />}</Field>;
}

export function SelectField({ label, hint, error, children, ...rest }: Shared & SelectHTMLAttributes<HTMLSelectElement>) {
  return <Field label={label} hint={hint} error={error}>{(p) => <select {...p} {...rest}>{children}</select>}</Field>;
}

export function CheckField({ label, ...rest }: { label: string } & Omit<InputHTMLAttributes<HTMLInputElement>, "type">) {
  return <label className={styles.check}><input type="checkbox" {...rest} />{label}</label>;
}
