CREATE DATABASE JACO_SalesDiscount;
GO
USE JACO_SalesDiscount;
GO
SET QUOTED_IDENTIFIER ON;
GO

CREATE SEQUENCE dbo.SalesDiscountIdSequence
    AS BIGINT
    START WITH 2000000000
    INCREMENT BY 1
    MINVALUE 2000000000
    NO MAXVALUE
    CACHE 50;
GO

CREATE TABLE dbo.Branches
(
    Id INT IDENTITY PRIMARY KEY,
    Code NVARCHAR(20) NOT NULL,
    Name NVARCHAR(100) NOT NULL,
    CompanyCode NVARCHAR(20) NOT NULL,
    CompanyName NVARCHAR(150) NOT NULL,
    AccountEmail NVARCHAR(200) NULL,
    Active BIT NOT NULL DEFAULT 1
);
CREATE UNIQUE INDEX UX_Branches_Code ON dbo.Branches(Code);
GO

CREATE TABLE dbo.SalesDiscountLookupValues
(
    Id INT IDENTITY PRIMARY KEY,
    LookupType NVARCHAR(40) NOT NULL,
    Value NVARCHAR(80) NOT NULL,
    DisplayText NVARCHAR(150) NOT NULL,
    SortOrder INT NOT NULL,
    Active BIT NOT NULL DEFAULT 1
);
CREATE UNIQUE INDEX UX_SalesDiscountLookupValues_Type_Value ON dbo.SalesDiscountLookupValues(LookupType,Value);
GO

CREATE TABLE dbo.SalesDiscountRequests
(
    Id BIGINT IDENTITY PRIMARY KEY,
    RequestId BIGINT NULL,
    RequestNumber AS RIGHT(REPLICATE('0',10) + CONVERT(varchar(10), RequestId), 10) PERSISTED,

    Branch NVARCHAR(100) NOT NULL,
    Company NVARCHAR(150) NOT NULL,
    CustomerName NVARCHAR(200) NOT NULL,
    DiscountReason NVARCHAR(150) NULL,
    DiscountNotes NVARCHAR(MAX) NULL,
    VehicleModel NVARCHAR(100) NOT NULL,
    ModelYear INT NULL,
    CommissionNumber NVARCHAR(50) NULL,
    Vin NVARCHAR(50) NULL,
    SalesChannel NVARCHAR(50) NOT NULL,
    OrderType NVARCHAR(50) NOT NULL,
    SpecialOrder NVARCHAR(10) NULL,
    DaysInStock INT NULL,
    DaysReserved INT NULL,
    SellingPrice DECIMAL(18,2) NULL,
    CostPrice DECIMAL(18,2) NULL,
    RequestedDiscountPercent DECIMAL(9,4) NOT NULL,
    RequestedDiscountAmount DECIMAL(18,2) NULL,
    CustomerFinalOffer DECIMAL(18,2) NULL,
    NetMargin DECIMAL(9,4) NULL,

    CreatorUserId INT NOT NULL,
    CreatorUserName NVARCHAR(100) NOT NULL,

    Status NVARCHAR(40) NOT NULL,
    ApprovalWorkflowNo NVARCHAR(50) NULL,
    ApprovalStatus NVARCHAR(40) NULL,
    ApprovalCurrentLevel INT NULL,

    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE UNIQUE INDEX UX_SalesDiscountRequests_RequestId ON dbo.SalesDiscountRequests(RequestId);
CREATE INDEX IX_SalesDiscountRequests_Creator ON dbo.SalesDiscountRequests(CreatorUserId,CreatedAt);
CREATE INDEX IX_SalesDiscountRequests_ApprovalWorkflow ON dbo.SalesDiscountRequests(ApprovalWorkflowNo);
GO

CREATE TABLE dbo.SalesDiscountAttachments
(
    Id BIGINT IDENTITY PRIMARY KEY,
    SalesDiscountRequestId BIGINT NOT NULL,
    OriginalFileName NVARCHAR(260) NOT NULL,
    StoredFileName NVARCHAR(260) NOT NULL,
    ContentType NVARCHAR(150) NOT NULL,
    FileSize BIGINT NOT NULL,
    UploadedByUserName NVARCHAR(100) NOT NULL,
    UploadedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    TransferStatus NVARCHAR(30) NOT NULL DEFAULT(N'Pending'),
    ApprovalAttachmentId BIGINT NULL,
    TransferredAt DATETIME2 NULL,

    CONSTRAINT FK_SalesDiscountAttachments_Request
        FOREIGN KEY(SalesDiscountRequestId)
        REFERENCES dbo.SalesDiscountRequests(Id)
);
CREATE INDEX IX_SalesDiscountAttachments_Request ON dbo.SalesDiscountAttachments(SalesDiscountRequestId,UploadedAt);
GO

-- Seed branches (per the doc: 5 branches under Juffali Automotive across a few
-- companies -- adjust codes/emails to the real org chart when known).
INSERT dbo.Branches(Code,Name,CompanyCode,CompanyName,AccountEmail) VALUES
(N'RUH-URB',N'Urobah - Riyadh',N'JAC',N'Juffali Automotive Company',N'accounts.riyadh@example.com'),
(N'JED-AML',N'Automall - Jeddah',N'JAC',N'Juffali Automotive Company',N'accounts.jeddah@example.com'),
(N'DMM-MAIN',N'Dammam - Main',N'JAC',N'Juffali Automotive Company',N'accounts.dammam@example.com'),
(N'MAK-MAIN',N'Makkah - Main',N'JAM',N'Juffali Automotive Mercedes',N'accounts.makkah@example.com'),
(N'MED-MAIN',N'Madinah - Main',N'JAM',N'Juffali Automotive Mercedes',N'accounts.madinah@example.com');
GO

INSERT dbo.SalesDiscountLookupValues(LookupType,Value,DisplayText,SortOrder) VALUES
(N'DiscountReason',N'01',N'01 - Damage',1),
(N'DiscountReason',N'02',N'02 - Delay in delivery',2),
(N'DiscountReason',N'03',N'03 - Competition with other brands',3),
(N'DiscountReason',N'04',N'04 - For bank approval',4),
(N'DiscountReason',N'05',N'05 - Over-aged stock',5),
(N'DiscountReason',N'06',N'06 - Slow-moving engine',6),
(N'SalesChannel',N'Retail',N'Retail',1),
(N'SalesChannel',N'Leasing',N'Leasing',2),
(N'SalesChannel',N'Affinity',N'Affinity',3),
(N'SalesChannel',N'Government',N'Government',4),
(N'OrderType',N'SalesOrder',N'Sales Order',1),
(N'OrderType',N'FleetOrder',N'Fleet Order',2);
GO

PRINT 'JACO_SalesDiscount database created successfully.';
GO
