-- Clears the delegation feature's runtime data: impersonation sessions, the audit trail and
-- every delegation. Schema is untouched; this only empties rows that testing created, so a
-- machine matches a freshly-migrated one.
--
-- None of it is seeded, so on a colleague's machine these tables are already empty and
-- running this is a no-op.
--
-- Order matters: DelegatedActions and ImpersonationSessions both hold a foreign key to
-- ManagerDelegations, and the key is NoAction, so the children go first.
--
-- Run it from your IDE's database console (Rider: the Database tool window), or:
--   Windows  sqlcmd -S "(localdb)\MSSQLLocalDB" -d CompanyEmployees -i scripts\sql\reset-delegation-test-data.sql
--   Docker   docker exec -i sql1 /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -d CompanyEmployees -i /dev/stdin < scripts/sql/reset-delegation-test-data.sql

SET NOCOUNT ON;

DELETE FROM DelegatedActions;
DELETE FROM ImpersonationSessions;
DELETE FROM ManagerDelegations;

-- Leave requests created while testing the delegated-approval flow.
DELETE FROM LeaveRequests WHERE Reason = 'Test request for delegated approval';

SELECT 'DelegatedActions'      AS TableName, COUNT(*) AS Remaining FROM DelegatedActions
UNION ALL SELECT 'ImpersonationSessions', COUNT(*) FROM ImpersonationSessions
UNION ALL SELECT 'ManagerDelegations',    COUNT(*) FROM ManagerDelegations;
