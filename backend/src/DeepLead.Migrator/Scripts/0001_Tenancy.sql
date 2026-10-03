-- Tenants and users (custom auth, no ASP.NET Identity / EF)

CREATE TABLE dbo.Tenants
(
    Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Tenants PRIMARY KEY,
    Name          NVARCHAR(200)     NOT NULL,
    IsActive      BIT               NOT NULL CONSTRAINT DF_Tenants_IsActive DEFAULT (1),
    CreatedAt     DATETIME2(0)      NOT NULL CONSTRAINT DF_Tenants_CreatedAt DEFAULT (SYSUTCDATETIME())
);

CREATE TABLE dbo.Users
(
    Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
    TenantId      INT               NOT NULL CONSTRAINT FK_Users_Tenants REFERENCES dbo.Tenants(Id),
    Email         NVARCHAR(320)     NOT NULL,
    FullName      NVARCHAR(200)     NOT NULL,
    PasswordHash  NVARCHAR(100)     NOT NULL,           -- BCrypt
    Role          VARCHAR(20)       NOT NULL CONSTRAINT CK_Users_Role CHECK (Role IN ('Admin', 'TenantAdmin', 'User')),
    IsActive      BIT               NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT (1),
    LastLoginAt   DATETIME2(0)      NULL,
    CreatedAt     DATETIME2(0)      NOT NULL CONSTRAINT DF_Users_CreatedAt DEFAULT (SYSUTCDATETIME())
);

CREATE UNIQUE INDEX UX_Users_Email ON dbo.Users(Email);
CREATE INDEX IX_Users_TenantId ON dbo.Users(TenantId);

CREATE TABLE dbo.RefreshTokens
(
    Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RefreshTokens PRIMARY KEY,
    UserId        INT                  NOT NULL CONSTRAINT FK_RefreshTokens_Users REFERENCES dbo.Users(Id),
    TokenHash     BINARY(32)           NOT NULL,        -- SHA-256 of the token, never the raw token
    ExpiresAt     DATETIME2(0)         NOT NULL,
    RevokedAt     DATETIME2(0)         NULL,
    CreatedAt     DATETIME2(0)         NOT NULL CONSTRAINT DF_RefreshTokens_CreatedAt DEFAULT (SYSUTCDATETIME())
);

CREATE UNIQUE INDEX UX_RefreshTokens_TokenHash ON dbo.RefreshTokens(TokenHash);
