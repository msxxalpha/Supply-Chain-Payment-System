-- Additive, idempotent upgrade for supplier non-participation.
-- Existing suppliers default to participation (0); no supplier or receipt data is removed.
IF COL_LENGTH(N'dbo.Suppliers',N'IsNonParticipating') IS NULL
    ALTER TABLE dbo.Suppliers ADD IsNonParticipating bit NOT NULL
        CONSTRAINT DF_Suppliers_IsNonParticipating DEFAULT(0) WITH VALUES;
