SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF COL_LENGTH('dbo.MaintenancePayments', 'Status') IS NULL
    ALTER TABLE dbo.MaintenancePayments ADD Status NVARCHAR(50) NOT NULL CONSTRAINT DF_MaintenancePayments_Status DEFAULT('pending_approval');
GO
IF COL_LENGTH('dbo.MaintenancePayments', 'ApprovedByUserId') IS NULL
    ALTER TABLE dbo.MaintenancePayments ADD ApprovedByUserId UNIQUEIDENTIFIER NULL;
GO
IF COL_LENGTH('dbo.MaintenancePayments', 'ApprovedAtUtc') IS NULL
    ALTER TABLE dbo.MaintenancePayments ADD ApprovedAtUtc DATETIME2 NULL;
GO
IF COL_LENGTH('dbo.MaintenancePayments', 'SubmittedByResident') IS NULL
    ALTER TABLE dbo.MaintenancePayments ADD SubmittedByResident BIT NOT NULL CONSTRAINT DF_MaintenancePayments_SubmittedByResident DEFAULT(0);
GO

UPDATE dbo.MaintenancePayments SET Status = 'approved' WHERE Status IS NULL OR Status = '';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MaintenancePayment_CommunityId_Status' AND object_id = OBJECT_ID('dbo.MaintenancePayments'))
    CREATE INDEX IX_MaintenancePayment_CommunityId_Status ON dbo.MaintenancePayments (CommunityId, Status);
GO

DECLARE @perms TABLE (Id UNIQUEIDENTIFIER, ResourceType NVARCHAR(100), Action INT);
INSERT INTO @perms (Id, ResourceType, Action) VALUES
 ('2df42b32-5f2d-13ae-5ca3-ae9269607fcb', 'MaintenanceCharge', 0),
 ('a9fa576f-0c47-20e3-b35c-bf35b81c8b71', 'MaintenanceCharge', 1),
 ('bd075e2a-820d-1aef-11a1-b782cfeac22f', 'MaintenanceCharge', 2),
 ('fd10134c-4d43-05f8-fcfa-d0fbfa445ea8', 'MaintenanceBill', 0),
 ('f73b0960-d986-ee0c-5d9e-a360e4621c7b', 'MaintenanceBill', 1),
 ('92029ab9-a721-5490-1fcb-937c5e51db6d', 'MaintenanceBill', 2),
 ('f6d8bf6e-601e-a1e9-92af-61624871c292', 'MaintenancePayment', 0),
 ('50a4a386-09ef-a29f-0389-9bf26200ddaa', 'MaintenancePayment', 1),
 ('d5c27078-7cf6-0901-bf44-022decef31d7', 'MaintenancePayment', 4);

INSERT INTO dbo.Permissions (Id, ResourceType, Action, IsDeleted, CreatedAtUtc, CreatedBy)
SELECT p.Id, p.ResourceType, p.Action, 0, SYSUTCDATETIME(), 'seed'
FROM @perms p
WHERE NOT EXISTS (SELECT 1 FROM dbo.Permissions e WHERE e.ResourceType = p.ResourceType AND e.Action = p.Action);
GO

DECLARE @resident UNIQUEIDENTIFIER = '46769A8A-1B2C-468A-8B7C-958E2429ED76';
DECLARE @admin UNIQUEIDENTIFIER = '1C5CD140-A850-49C7-A935-505EBC3B750E';

INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
SELECT @resident, pm.Id
FROM dbo.Permissions pm
WHERE ((pm.ResourceType = 'MaintenanceCharge' AND pm.Action = 0)
    OR (pm.ResourceType = 'MaintenanceBill' AND pm.Action = 0)
    OR (pm.ResourceType = 'MaintenancePayment' AND pm.Action IN (0, 1)))
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId = @resident AND rp.PermissionId = pm.Id);

INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
SELECT @admin, pm.Id
FROM dbo.Permissions pm
WHERE pm.ResourceType IN ('MaintenanceCharge', 'MaintenanceBill', 'MaintenancePayment')
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId = @admin AND rp.PermissionId = pm.Id);
GO
