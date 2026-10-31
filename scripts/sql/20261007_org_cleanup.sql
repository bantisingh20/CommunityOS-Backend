SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

DECLARE @keepC1 UNIQUEIDENTIFIER = '6AB0F9B2-2CC0-4E3E-92C9-6D334AA3EB60';
DECLARE @keepC2 UNIQUEIDENTIFIER = 'A891C739-E4EA-4D62-B13E-9B0536F982E3';
DECLARE @keepO1 UNIQUEIDENTIFIER = '5F8CB027-1FF8-479E-AF72-2967E951CA7D';
DECLARE @keepO2 UNIQUEIDENTIFIER = 'EBCF8BCF-7A24-4D88-913C-47B75035D554';

BEGIN TRY
    BEGIN TRAN;

    DECLARE @sql NVARCHAR(MAX) = N'';

    SELECT @sql = @sql + N'ALTER TABLE ' + QUOTENAME(t.name) + N' NOCHECK CONSTRAINT ALL;' + CHAR(10)
    FROM sys.tables t
    JOIN sys.columns c ON c.object_id = t.object_id
    WHERE c.name = 'CommunityId';
    EXEC sp_executesql @sql;

    SET @sql = N'';
    SELECT @sql = @sql + N'DELETE FROM ' + QUOTENAME(t.name)
        + N' WHERE CommunityId NOT IN (@k1,@k2);' + CHAR(10)
    FROM sys.tables t
    JOIN sys.columns c ON c.object_id = t.object_id
    WHERE c.name = 'CommunityId';
    EXEC sp_executesql @sql, N'@k1 UNIQUEIDENTIFIER, @k2 UNIQUEIDENTIFIER', @k1 = @keepC1, @k2 = @keepC2;

    DELETE FROM dbo.Configurations
    WHERE OrganizationId IS NOT NULL AND OrganizationId NOT IN (@keepO1, @keepO2);

    DELETE FROM dbo.UserRoles
    WHERE CommunityId IS NOT NULL AND CommunityId NOT IN (@keepC1, @keepC2);

    DELETE FROM dbo.Organizations
    WHERE Id NOT IN (@keepO1, @keepO2);

    SET @sql = N'';
    SELECT @sql = @sql + N'ALTER TABLE ' + QUOTENAME(t.name) + N' WITH CHECK CHECK CONSTRAINT ALL;' + CHAR(10)
    FROM sys.tables t
    JOIN sys.columns c ON c.object_id = t.object_id
    WHERE c.name = 'CommunityId';
    EXEC sp_executesql @sql;

    COMMIT;
    PRINT 'Org cleanup committed.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    DECLARE @msg NVARCHAR(4000) = ERROR_MESSAGE();
    PRINT 'ROLLED BACK: ' + @msg;
    THROW;
END CATCH;
GO
