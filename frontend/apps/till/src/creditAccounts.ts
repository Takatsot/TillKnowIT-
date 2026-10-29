import type { Api } from "@tillknowit/api-client";
import type { Cents } from "@tillknowit/domain";
import type { KeyValueStore } from "./localData";
import type { DeviceConfig } from "./syncRunner";

/** A credit customer with what they owe (CreditAccountBalance in ShopCore.Contracts). OverLimit only warns; it never stops a sale. */
export interface CreditAccount {
  id: string;
  name: string;
  phone: string | null;
  creditLimitCents: Cents | null;
  balanceCents: Cents;
  overLimit: boolean;
}

const KEY = "creditAccounts";

export interface CreditAccountsLoad {
  accounts: CreditAccount[];
  /** True when the list is the copy kept from the last time the till was online. */
  cached: boolean;
}

/** The credit customers: from the server when the till can reach it (and the copy is refreshed), otherwise the copy kept on the till. */
export async function loadCreditAccounts(api: Api, device: DeviceConfig | null, kv: KeyValueStore | undefined): Promise<CreditAccountsLoad> {
  if (device) {
    const fresh = await api.get<CreditAccount[]>(`/shops/${device.shopId}/credit-accounts`);
    if (fresh.ok) {
      await kv?.set(KEY, fresh.value);
      return { accounts: fresh.value, cached: false };
    }
  }
  return { accounts: (await kv?.get<CreditAccount[]>(KEY)) ?? [], cached: true };
}

export const cachedCreditAccounts = async (kv: KeyValueStore | undefined): Promise<CreditAccount[]> => (await kv?.get<CreditAccount[]>(KEY)) ?? [];
