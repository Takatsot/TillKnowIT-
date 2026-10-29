import { useApi, useLoad, useSession } from "@tillknowit/app-kit";

export interface ProductOption { id: string; name: string }

/** The supplier's own products, for choosing one in a deal. */
export function useProducts(supplierId: string) {
  const api = useApi();
  const { personId } = useSession();
  return useLoad(() => api.get<ProductOption[]>(`/suppliers/${supplierId}/products`, { callerPersonId: personId }), [supplierId, personId]);
}
