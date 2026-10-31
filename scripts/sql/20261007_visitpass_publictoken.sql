SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.VisitPasses') AND name = 'PublicToken'
)
BEGIN
    ALTER TABLE dbo.VisitPasses ADD PublicToken NVARCHAR(128) NOT NULL CONSTRAINT DF_VisitPasses_PublicToken DEFAULT('');
END;
GO

UPDATE dbo.VisitPasses
SET PublicToken = LOWER(CONVERT(NVARCHAR(36), NEWID())) + LOWER(CONVERT(NVARCHAR(36), NEWID()))
WHERE PublicToken = '' OR PublicToken IS NULL;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'UX_VisitPass_PublicToken' AND object_id = OBJECT_ID('dbo.VisitPasses')
)
BEGIN
    CREATE UNIQUE INDEX UX_VisitPass_PublicToken
        ON dbo.VisitPasses (PublicToken)
        WHERE PublicToken <> '';
END;
GO
