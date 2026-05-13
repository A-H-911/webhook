-- One-shot cleanup for tokens soft-deleted by the old DeleteTokenCommandHandler
-- (which called token.Deactivate() instead of hard-deleting the row).
-- After deploying the hard-delete fix, run this once against the production DB
-- to remove orphaned WebhookRequest rows that are still inflating dashboard metrics.
--
-- Idempotent: safe to run multiple times.
-- Order matters: delete child rows before parent rows to avoid FK violation.

DELETE FROM WebhookRequests
WHERE TokenId IN (SELECT Id FROM WebhookTokens WHERE IsActive = 0);

DELETE FROM WebhookTokens WHERE IsActive = 0;
