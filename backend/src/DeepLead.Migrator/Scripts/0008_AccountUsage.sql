-- Daily usage counter per connected account, so logged-in scraping stays under a safe daily cap
-- (LinkedIn restricts accounts that search too much).
ALTER TABLE dbo.ConnectedAccounts ADD
    UsageDate   DATE NULL,
    UsageCount  INT  NOT NULL CONSTRAINT DF_ConnectedAccounts_UsageCount DEFAULT (0);
