import { Button, Inline } from "@tillknowit/design-system";

/** A row of buttons that switches between the sub-views of one area. */
export function Tabs<K extends string>({ label, tabs, current, onChange }: { label: string; tabs: readonly { id: K; label: string }[]; current: K; onChange: (id: K) => void }) {
  return (
    <nav aria-label={label}>
      <Inline>
        {tabs.map((tab) => (
          <Button key={tab.id} variant={tab.id === current ? "primary" : "secondary"} aria-current={tab.id === current ? "page" : undefined} onClick={() => onChange(tab.id)}>{tab.label}</Button>
        ))}
      </Inline>
    </nav>
  );
}
