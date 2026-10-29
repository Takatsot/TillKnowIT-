import { useApi, useMessages, useSession } from "@tillknowit/app-kit";
import { Button, Card, Inline, Notice, SelectField, Stack, TextField } from "@tillknowit/design-system";
import { useState } from "react";
import { messages } from "./messages";
import type { Holder, HolderWithCode, School } from "./types";

/** Adds one learner or teacher: POST /schools/{id}/holders (AddHolderRequest). The link code comes back once. */
export function AddHolderCard({ school, onAdded }: { school: School; onAdded: () => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const [firstName, setFirstName] = useState("");
  const [lastName, setLastName] = useState("");
  const [grade, setGrade] = useState("");
  const [className, setClassName] = useState("");
  const [type, setType] = useState("learner");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [added, setAdded] = useState<HolderWithCode | null>(null);

  async function submit() {
    setBusy(true);
    setError(null);
    setAdded(null);
    const r = await api.post<HolderWithCode>(`/schools/${school.id}/holders`, {
      callerPersonId: personId, firstName: firstName.trim(), lastName: lastName.trim(), grade: grade.trim() || null, className: className.trim() || null, type,
    });
    setBusy(false);
    if (!r.ok) return setError(r.message);
    setAdded(r.value);
    setFirstName("");
    setLastName("");
    setGrade("");
    setClassName("");
    onAdded();
  }

  return (
    <Card title={t("addHolderHeading")}>
      <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
        <Stack>
          <Inline>
            <TextField label={t("fieldFirstName")} value={firstName} onChange={(e) => setFirstName(e.target.value)} />
            <TextField label={t("fieldLastName")} value={lastName} onChange={(e) => setLastName(e.target.value)} />
          </Inline>
          <Inline>
            <TextField label={t("colGrade")} value={grade} onChange={(e) => setGrade(e.target.value)} />
            <TextField label={t("colClass")} value={className} onChange={(e) => setClassName(e.target.value)} />
            <SelectField label={t("colType")} value={type} onChange={(e) => setType(e.target.value)}>
              <option value="learner">{t("typeLearner")}</option>
              <option value="teacher">{t("typeTeacher")}</option>
            </SelectField>
          </Inline>
          {error && <Notice kind="danger">{error}</Notice>}
          <Button type="submit" disabled={busy || firstName.trim() === "" || lastName.trim() === ""}>{t("addHolderRun")}</Button>
          {added && (
            <Notice kind="warning">
              {t("addHolderDone", { name: `${added.holder.firstName} ${added.holder.lastName}` })} {t("addHolderCode")} <code>{added.linkCode}</code>. {t("codesWarning")}
            </Notice>
          )}
        </Stack>
      </form>
    </Card>
  );
}

/** Changes one person: PATCH /holders/{id} (UpdateHolderRequest). A blank grade or class clears it. */
export function EditHolderCard({ holder, onSaved, onCancel }: { holder: Holder; onSaved: () => void; onCancel: () => void }) {
  const t = useMessages(messages);
  const api = useApi();
  const { personId } = useSession();
  const [firstName, setFirstName] = useState(holder.firstName);
  const [lastName, setLastName] = useState(holder.lastName);
  const [grade, setGrade] = useState(holder.grade ?? "");
  const [className, setClassName] = useState(holder.className ?? "");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit() {
    setBusy(true);
    setError(null);
    const r = await api.patch<Holder>(`/holders/${holder.id}`, { callerPersonId: personId, firstName: firstName.trim(), lastName: lastName.trim(), grade: grade.trim(), className: className.trim() });
    setBusy(false);
    if (!r.ok) return setError(r.message);
    onSaved();
  }

  return (
    <Card title={t("editHolderHeading", { name: `${holder.firstName} ${holder.lastName}` })}>
      <form onSubmit={(e) => { e.preventDefault(); void submit(); }}>
        <Stack>
          <Inline>
            <TextField label={t("fieldFirstName")} value={firstName} onChange={(e) => setFirstName(e.target.value)} />
            <TextField label={t("fieldLastName")} value={lastName} onChange={(e) => setLastName(e.target.value)} />
            <TextField label={t("colGrade")} value={grade} onChange={(e) => setGrade(e.target.value)} />
            <TextField label={t("colClass")} value={className} onChange={(e) => setClassName(e.target.value)} />
          </Inline>
          {error && <Notice kind="danger">{error}</Notice>}
          <Inline>
            <Button type="submit" disabled={busy || firstName.trim() === "" || lastName.trim() === ""}>{t("editHolderSave")}</Button>
            <Button variant="quiet" onClick={onCancel}>{t("editHolderCancel")}</Button>
          </Inline>
        </Stack>
      </form>
    </Card>
  );
}
