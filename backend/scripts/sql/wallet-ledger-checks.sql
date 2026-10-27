-- Wallet ledger checks. Run against the wallet database after the Wallet migration; safe to run again.
--
-- Why a view and not a trigger: SQL Server has no deferred constraints, and EF inserts a transaction's entries in the same
-- SaveChanges but not always in the same statement, so a trigger that insisted on a zero sum per statement would refuse valid
-- transactions. The zero-sum rule is enforced in the domain (LedgerTransaction.Create refuses anything that does not balance) and
-- checked here and by the daily reconciliation (IWalletStore.UnbalancedTransactions), which blocks payouts if it ever finds one.

CREATE OR ALTER VIEW wallet.v_unbalanced_transactions AS
SELECT e.transaction_id, SUM(e.amount_cents) AS difference_cents, COUNT(*) AS entries
FROM wallet.ledger_entries AS e
GROUP BY e.transaction_id
HAVING SUM(e.amount_cents) <> 0;
GO

-- Every account balance, for a spot check against the partner's statement. The partner float should equal minus what the partner holds.
CREATE OR ALTER VIEW wallet.v_account_balances AS
SELECT a.id AS account_id, a.type, a.owner_id, COALESCE(SUM(e.amount_cents), 0) AS balance_cents
FROM wallet.ledger_accounts AS a
LEFT JOIN wallet.ledger_entries AS e ON e.account_id = a.id
GROUP BY a.id, a.type, a.owner_id;
GO
