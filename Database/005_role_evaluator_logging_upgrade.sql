-- Add evaluator activity logging opt-in per role.
-- Existing roles intentionally default to disabled (0) so they must be opted in explicitly.
IF COL_LENGTH(N'dbo.AppRoles',N'IsEvaluatorLogEnabled') IS NULL
    ALTER TABLE dbo.AppRoles ADD IsEvaluatorLogEnabled bit NOT NULL
        CONSTRAINT DF_AppRoles_IsEvaluatorLogEnabled DEFAULT(0) WITH VALUES;
