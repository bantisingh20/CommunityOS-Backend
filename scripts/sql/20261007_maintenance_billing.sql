SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID('dbo.MaintenanceCharges', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MaintenanceCharges
    (
        Id              UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_MaintenanceCharges PRIMARY KEY,
        CommunityId     UNIQUEIDENTIFIER NOT NULL,
        UnitId          UNIQUEIDENTIFIER NOT NULL,
        Amount          DECIMAL(18,2) NOT NULL CONSTRAINT DF_MaintenanceCharges_Amount DEFAULT(0),
        Label           NVARCHAR(200) NOT NULL CONSTRAINT DF_MaintenanceCharges_Label DEFAULT(''),
        IsActive        BIT NOT NULL CONSTRAINT DF_MaintenanceCharges_IsActive DEFAULT(1),
        IsDeleted       BIT NOT NULL CONSTRAINT DF_MaintenanceCharges_IsDeleted DEFAULT(0),
        DeletedAtUtc    DATETIME2 NULL,
        RowVersion      ROWVERSION NOT NULL,
        CreatedAtUtc    DATETIME2 NOT NULL,
        CreatedBy       NVARCHAR(MAX) NULL,
        UpdatedAtUtc    DATETIME2 NULL,
        UpdatedBy       NVARCHAR(MAX) NULL
    );
END;
GO

IF OBJECT_ID('dbo.MaintenanceBills', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MaintenanceBills
    (
        Id              UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_MaintenanceBills PRIMARY KEY,
        CommunityId     UNIQUEIDENTIFIER NOT NULL,
        UnitId          UNIQUEIDENTIFIER NULL,
        PeriodYear      INT NOT NULL,
        PeriodMonth     INT NOT NULL,
        Amount          DECIMAL(18,2) NOT NULL CONSTRAINT DF_MaintenanceBills_Amount DEFAULT(0),
        AmountPaid      DECIMAL(18,2) NOT NULL CONSTRAINT DF_MaintenanceBills_AmountPaid DEFAULT(0),
        Label           NVARCHAR(200) NOT NULL CONSTRAINT DF_MaintenanceBills_Label DEFAULT(''),
        Status          NVARCHAR(50) NOT NULL CONSTRAINT DF_MaintenanceBills_Status DEFAULT('unpaid'),
        DueDateUtc      DATETIME2 NULL,
        IssuedAtUtc     DATETIME2 NOT NULL,
        IsDeleted       BIT NOT NULL CONSTRAINT DF_MaintenanceBills_IsDeleted DEFAULT(0),
        DeletedAtUtc    DATETIME2 NULL,
        RowVersion      ROWVERSION NOT NULL,
        CreatedAtUtc    DATETIME2 NOT NULL,
        CreatedBy       NVARCHAR(MAX) NULL,
        UpdatedAtUtc    DATETIME2 NULL,
        UpdatedBy       NVARCHAR(MAX) NULL
    );
END;
GO

IF OBJECT_ID('dbo.MaintenancePayments', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MaintenancePayments
    (
        Id                  UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_MaintenancePayments PRIMARY KEY,
        CommunityId         UNIQUEIDENTIFIER NOT NULL,
        MaintenanceBillId   UNIQUEIDENTIFIER NOT NULL,
        UnitId              UNIQUEIDENTIFIER NULL,
        Amount              DECIMAL(18,2) NOT NULL CONSTRAINT DF_MaintenancePayments_Amount DEFAULT(0),
        Method              NVARCHAR(50) NOT NULL,
        Reference           NVARCHAR(200) NOT NULL CONSTRAINT DF_MaintenancePayments_Reference DEFAULT(''),
        PaidAtUtc           DATETIME2 NOT NULL,
        IsDeleted           BIT NOT NULL CONSTRAINT DF_MaintenancePayments_IsDeleted DEFAULT(0),
        DeletedAtUtc        DATETIME2 NULL,
        RowVersion          ROWVERSION NOT NULL,
        CreatedAtUtc        DATETIME2 NOT NULL,
        CreatedBy           NVARCHAR(MAX) NULL,
        UpdatedAtUtc        DATETIME2 NULL,
        UpdatedBy           NVARCHAR(MAX) NULL
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_MaintenancePayments_Bill')
   AND OBJECT_ID('dbo.MaintenancePayments', 'U') IS NOT NULL
   AND OBJECT_ID('dbo.MaintenanceBills', 'U') IS NOT NULL
BEGIN
    ALTER TABLE dbo.MaintenancePayments
        ADD CONSTRAINT FK_MaintenancePayments_Bill
        FOREIGN KEY (MaintenanceBillId) REFERENCES dbo.MaintenanceBills (Id);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MaintenanceCharge_CommunityId_UnitId' AND object_id = OBJECT_ID('dbo.MaintenanceCharges'))
BEGIN
    CREATE INDEX IX_MaintenanceCharge_CommunityId_UnitId ON dbo.MaintenanceCharges (CommunityId, UnitId);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_MaintenanceCharge_UnitId_Active' AND object_id = OBJECT_ID('dbo.MaintenanceCharges'))
BEGIN
    CREATE UNIQUE INDEX UX_MaintenanceCharge_UnitId_Active
        ON dbo.MaintenanceCharges (UnitId)
        WHERE IsActive = 1 AND IsDeleted = 0;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MaintenanceBill_CommunityId_Status_Period' AND object_id = OBJECT_ID('dbo.MaintenanceBills'))
BEGIN
    CREATE INDEX IX_MaintenanceBill_CommunityId_Status_Period ON dbo.MaintenanceBills (CommunityId, Status, PeriodYear, PeriodMonth);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MaintenanceBill_CommunityId_UnitId_Period' AND object_id = OBJECT_ID('dbo.MaintenanceBills'))
BEGIN
    CREATE INDEX IX_MaintenanceBill_CommunityId_UnitId_Period ON dbo.MaintenanceBills (CommunityId, UnitId, PeriodYear, PeriodMonth);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_MaintenanceBill_UnitId_Period' AND object_id = OBJECT_ID('dbo.MaintenanceBills'))
BEGIN
    CREATE UNIQUE INDEX UX_MaintenanceBill_UnitId_Period
        ON dbo.MaintenanceBills (UnitId, PeriodYear, PeriodMonth)
        WHERE IsDeleted = 0 AND UnitId IS NOT NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MaintenancePayment_CommunityId_MaintenanceBillId' AND object_id = OBJECT_ID('dbo.MaintenancePayments'))
BEGIN
    CREATE INDEX IX_MaintenancePayment_CommunityId_MaintenanceBillId ON dbo.MaintenancePayments (CommunityId, MaintenanceBillId);
END;
GO
