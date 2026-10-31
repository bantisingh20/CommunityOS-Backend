SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'admin.maintenanceBilling')
BEGIN
    INSERT INTO dbo.MenuItems
        (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES
        (NEWID(), NULL, 'admin.maintenanceBilling', 'Maintenance Billing', 'DollarOutlined', '/maintenance-billing', 1, 'MaintenanceBill', '0', 61, 1, 0, 1, SYSUTCDATETIME(), 'seed');
END;
GO

UPDATE dbo.MenuItems
SET Label = 'Help Desk', UpdatedAtUtc = SYSUTCDATETIME(), UpdatedBy = 'seed'
WHERE [Key] = 'admin.maintenance' AND Label <> 'Help Desk';
GO
