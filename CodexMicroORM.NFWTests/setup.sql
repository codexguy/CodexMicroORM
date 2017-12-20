/***********************************************************************
Copyright 2017 CodeX Enterprises LLC

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.

Major Changes:
12/2017    0.2     Initial release (Joel Champagne)
***********************************************************************/

USE master
GO

CREATE DATABASE [CodexMicroORMTest]
GO

USE [CodexMicroORMTest]
GO

CREATE SCHEMA [CEFTest] AUTHORIZATION dbo
GO

CREATE SCHEMA [History] AUTHORIZATION dbo
GO

SET ANSI_NULLS OFF
GO
SET QUOTED_IDENTIFIER OFF
GO

/*    ==Scripting Parameters==

    Source Server Version : SQL Server 2016 (13.0.1742)
    Source Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Source Database Engine Type : Standalone SQL Server

    Target Server Version : SQL Server 2016
    Target Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Target Database Engine Type : Standalone SQL Server
*/

SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [CEFTest].[PhoneType](
	[PhoneTypeID] [int] IDENTITY(1,1) NOT NULL,
	[PhoneTypeDesc] [varchar](100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[LastUpdatedBy] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[LastUpdatedDate] [datetime2](7) NOT NULL
) ON [PRIMARY]

GO


-- This is how it looks in source and would be executed in target

SET ANSI_NULLS OFF
GO
SET QUOTED_IDENTIFIER OFF
GO

/*    ==Scripting Parameters==

    Source Server Version : SQL Server 2016 (13.0.1742)
    Source Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Source Database Engine Type : Standalone SQL Server

    Target Server Version : SQL Server 2016
    Target Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Target Database Engine Type : Standalone SQL Server
*/

SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [CEFTest].[Phone](
	[PhoneID] [int] IDENTITY(1,1) NOT NULL,
	[PhoneTypeID] [int] NOT NULL,
	[Number] [varchar](20) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[LastUpdatedBy] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[LastUpdatedDate] [datetime2](7) NOT NULL,
	[PersonID] [int] NULL
) ON [PRIMARY]

GO


-- This is how it looks in source and would be executed in target

SET ANSI_NULLS OFF
GO
SET QUOTED_IDENTIFIER OFF
GO

/*    ==Scripting Parameters==

    Source Server Version : SQL Server 2016 (13.0.1742)
    Source Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Source Database Engine Type : Standalone SQL Server

    Target Server Version : SQL Server 2016
    Target Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Target Database Engine Type : Standalone SQL Server
*/

SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [History].[Person](
	[HistID] [bigint] IDENTITY(1,1) NOT NULL,
	[LastUpdatedBy] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[LastUpdatedDate] [datetime2](7) NOT NULL,
	[IsDeleted] [bit] NOT NULL,
	[PersonID] [int] NOT NULL,
	[Name] [varchar](100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[Age] [int] NOT NULL,
	[ParentPersonID] [int] NULL,
	[Gender] [char](1) COLLATE SQL_Latin1_General_CP1_CI_AS NULL
) ON [PRIMARY]

GO


-- This is how it looks in source and would be executed in target

SET ANSI_NULLS OFF
GO
SET QUOTED_IDENTIFIER OFF
GO

/*    ==Scripting Parameters==

    Source Server Version : SQL Server 2016 (13.0.1742)
    Source Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Source Database Engine Type : Standalone SQL Server

    Target Server Version : SQL Server 2016
    Target Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Target Database Engine Type : Standalone SQL Server
*/

SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [CEFTest].[Person](
	[PersonID] [int] IDENTITY(1,1) NOT NULL,
	[Name] [varchar](100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[Age] [int] NOT NULL,
	[ParentPersonID] [int] NULL,
	[LastUpdatedBy] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[LastUpdatedDate] [datetime2](7) NOT NULL,
	[Gender] [char](1) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	[IsDeleted] [bit] NOT NULL
) ON [PRIMARY]

GO


-- This is how it looks in source and would be executed in target
/*    ==Scripting Parameters==

    Source Server Version : SQL Server 2016 (13.0.1742)
    Source Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Source Database Engine Type : Standalone SQL Server

    Target Server Version : SQL Server 2016
    Target Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Target Database Engine Type : Standalone SQL Server
*/

/****** Object:  Index [PK__PhoneTyp__F39F5BB9543FAE7C]    Script Date: 12/19/2017 5:45:54 PM ******/
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[CEFTest].[PhoneType]') AND name = N'PK__PhoneTyp__F39F5BB9543FAE7C')
ALTER TABLE [CEFTest].[PhoneType] ADD  CONSTRAINT [PK__PhoneTyp__F39F5BB9543FAE7C] PRIMARY KEY CLUSTERED 
(
	[PhoneTypeID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO



-- This is how it looks in source and would be executed in target
/*    ==Scripting Parameters==

    Source Server Version : SQL Server 2016 (13.0.1742)
    Source Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Source Database Engine Type : Standalone SQL Server

    Target Server Version : SQL Server 2016
    Target Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Target Database Engine Type : Standalone SQL Server
*/

/****** Object:  Index [PK__Phone__F3EE4BD0FBB96228]    Script Date: 12/19/2017 5:45:54 PM ******/
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[CEFTest].[Phone]') AND name = N'PK__Phone__F3EE4BD0FBB96228')
ALTER TABLE [CEFTest].[Phone] ADD  CONSTRAINT [PK__Phone__F3EE4BD0FBB96228] PRIMARY KEY CLUSTERED 
(
	[PhoneID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO



-- This is how it looks in source and would be executed in target
/*    ==Scripting Parameters==

    Source Server Version : SQL Server 2016 (13.0.1742)
    Source Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Source Database Engine Type : Standalone SQL Server

    Target Server Version : SQL Server 2016
    Target Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Target Database Engine Type : Standalone SQL Server
*/

/****** Object:  Index [PK_Person_History]    Script Date: 12/19/2017 5:45:54 PM ******/
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[History].[Person]') AND name = N'PK_Person_History')
ALTER TABLE [History].[Person] ADD  CONSTRAINT [PK_Person_History] PRIMARY KEY CLUSTERED 
(
	[PersonID] ASC,
	[LastUpdatedDate] ASC,
	[HistID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO



-- This is how it looks in source and would be executed in target
/*    ==Scripting Parameters==

    Source Server Version : SQL Server 2016 (13.0.1742)
    Source Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Source Database Engine Type : Standalone SQL Server

    Target Server Version : SQL Server 2016
    Target Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Target Database Engine Type : Standalone SQL Server
*/

/****** Object:  Index [PK__Person__AA2FFB855E4FB0EC]    Script Date: 12/19/2017 5:45:54 PM ******/
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[CEFTest].[Person]') AND name = N'PK__Person__AA2FFB855E4FB0EC')
ALTER TABLE [CEFTest].[Person] ADD  CONSTRAINT [PK__Person__AA2FFB855E4FB0EC] PRIMARY KEY CLUSTERED 
(
	[PersonID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO



-- This is how it looks in source and would be executed in target
/*    ==Scripting Parameters==

    Source Server Version : SQL Server 2016 (13.0.1742)
    Source Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Source Database Engine Type : Standalone SQL Server

    Target Server Version : SQL Server 2016
    Target Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Target Database Engine Type : Standalone SQL Server
*/

/****** Object:  View [CEFTest].[Person_History]    Script Date: 12/19/2017 5:45:50 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE VIEW [CEFTest].[Person_History]
AS

WITH cte AS (
SELECT PersonID
	, Name
	, Age
	, ParentPersonID
	, Gender, LastUpdatedBy, LastUpdatedDate, IsDeleted
FROM [CEFTest].[Person]
UNION ALL
SELECT PersonID
	, Name
	, Age
	, ParentPersonID
	, Gender, LastUpdatedBy, LastUpdatedDate, IsDeleted
FROM [History].[Person]
)

SELECT
	PersonID
	, Name
	, Age
	, ParentPersonID
	, Gender, LastUpdatedBy, LastUpdatedDate, IsDeleted
	, ISNULL((SELECT TOP 1 b.LastUpdatedDate
		FROM cte b
		WHERE a.[PersonID] = b.[PersonID]
		AND b.LastUpdatedDate > a.LastUpdatedDate
		ORDER BY b.LastUpdatedDate), '12/31/9999') AS RowExpiryDate
FROM
	cte a
GO


-- This is how it looks in source and would be executed in target

/* Error: Value cannot be null.
Parameter name: type
*/

/*    ==Scripting Parameters==

    Source Server Version : SQL Server 2016 (13.0.1742)
    Source Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Source Database Engine Type : Standalone SQL Server

    Target Server Version : SQL Server 2016
    Target Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Target Database Engine Type : Standalone SQL Server
*/

/****** Object:  Index [NDX_PersonHistory_LastUpdatedDate]    Script Date: 12/19/2017 5:45:52 PM ******/
IF (SELECT COUNT(*) FROM sys.objects o JOIN sys.columns c ON o.[object_id]=c.[object_id] WHERE o.[object_id]=OBJECT_ID(N'[History].[Person]') AND c.[name] IN ('LastUpdatedDate'))>=1 AND NOT EXISTS (SELECT 0 FROM sys.indexes i WHERE i.name='NDX_PersonHistory_LastUpdatedDate' AND i.[object_id]=OBJECT_ID(N'[History].[Person]'))
CREATE NONCLUSTERED INDEX [NDX_PersonHistory_LastUpdatedDate] ON [History].[Person]
(
	[LastUpdatedDate] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO

-- This is how it looks in source and would be executed in target

/*    ==Scripting Parameters==

    Source Server Version : SQL Server 2016 (13.0.1742)
    Source Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Source Database Engine Type : Standalone SQL Server

    Target Server Version : SQL Server 2016
    Target Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Target Database Engine Type : Standalone SQL Server
*/

SET ANSI_PADDING ON

GO

/****** Object:  Index [NDX_Person_ParentID]    Script Date: 12/19/2017 5:45:54 PM ******/
IF (SELECT COUNT(*) FROM sys.objects o JOIN sys.columns c ON o.[object_id]=c.[object_id] WHERE o.[object_id]=OBJECT_ID(N'[CEFTest].[Person]') AND c.[name] IN ('ParentPersonID'))>=1 AND NOT EXISTS (SELECT 0 FROM sys.indexes i WHERE i.name='NDX_Person_ParentID' AND i.[object_id]=OBJECT_ID(N'[CEFTest].[Person]'))
CREATE NONCLUSTERED INDEX [NDX_Person_ParentID] ON [CEFTest].[Person]
(
	[ParentPersonID] ASC
)
INCLUDE ( 	[Gender],
	[Age]) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO

/****** Object:  Index [NDX_Person_Name]    Script Date: 12/19/2017 5:45:54 PM ******/
IF (SELECT COUNT(*) FROM sys.objects o JOIN sys.columns c ON o.[object_id]=c.[object_id] WHERE o.[object_id]=OBJECT_ID(N'[CEFTest].[Person]') AND c.[name] IN ('Name'))>=1 AND NOT EXISTS (SELECT 0 FROM sys.indexes i WHERE i.name='NDX_Person_Name' AND i.[object_id]=OBJECT_ID(N'[CEFTest].[Person]'))
CREATE NONCLUSTERED INDEX [NDX_Person_Name] ON [CEFTest].[Person]
(
	[Name] ASC
)
INCLUDE ( 	[Gender],
	[Age]) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO

-- This is how it looks in source and would be executed in target
/*    ==Scripting Parameters==

    Source Server Version : SQL Server 2016 (13.0.1742)
    Source Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Source Database Engine Type : Standalone SQL Server

    Target Server Version : SQL Server 2016
    Target Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Target Database Engine Type : Standalone SQL Server
*/

/****** Object:  Trigger [CEFTest].[tg_Person_u]    Script Date: 12/19/2017 5:45:53 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE  TRIGGER tg_Person_u ON [CEFTest].[Person] 
AFTER UPDATE 
AS
BEGIN
/*
<UDP><Name>SystemGenerated</Name><Value>True</Value></UDP>
*/

INSERT [History].[Person]
	(PersonID
	, Name
	, Age
	, ParentPersonID
	, Gender, LastUpdatedBy, LastUpdatedDate, IsDeleted)
SELECT
	PersonID
	, Name
	, Age
	, ParentPersonID
	, Gender, LastUpdatedBy, LastUpdatedDate, IsDeleted
FROM
	deleted d;

DELETE t
FROM
	[CEFTest].[Person] t
	JOIN inserted i
		ON t.[PersonID] = i.[PersonID]
		AND i.IsDeleted = 1;

END
GO

ALTER TABLE [CEFTest].[Person] ENABLE TRIGGER [tg_Person_u]
GO


-- This is how it looks in source and would be executed in target
/*    ==Scripting Parameters==

    Source Server Version : SQL Server 2016 (13.0.1742)
    Source Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Source Database Engine Type : Standalone SQL Server

    Target Server Version : SQL Server 2016
    Target Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Target Database Engine Type : Standalone SQL Server
*/

/****** Object:  Trigger [CEFTest].[tg_Person_d]    Script Date: 12/19/2017 5:45:54 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE  TRIGGER tg_Person_d ON [CEFTest].[Person] 
FOR DELETE
AS
BEGIN
/*
<UDP><Name>SystemGenerated</Name><Value>True</Value></UDP>
*/

INSERT [History].[Person]
	(PersonID
	, Name
	, Age
	, ParentPersonID
	, Gender, LastUpdatedBy, LastUpdatedDate, IsDeleted)
SELECT
	PersonID
	, Name
	, Age
	, ParentPersonID
	, Gender, LastUpdatedBy, LastUpdatedDate, IsDeleted
FROM
	deleted d
WHERE
	IsDeleted = 1;

IF @@ROWCOUNT = 0
  IF EXISTS (SELECT 0 FROM deleted)
    RAISERROR('You cannot perform a physical delete on Person.', 16, 1);

END
GO

ALTER TABLE [CEFTest].[Person] ENABLE TRIGGER [tg_Person_d]
GO


-- This is how it looks in source and would be executed in target
/*    ==Scripting Parameters==

    Source Server Version : SQL Server 2016 (13.0.1742)
    Source Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Source Database Engine Type : Standalone SQL Server

    Target Server Version : SQL Server 2016
    Target Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Target Database Engine Type : Standalone SQL Server
*/

ALTER TABLE [CEFTest].[Phone]  WITH NOCHECK ADD  CONSTRAINT [FK__Phone__PhoneType__04115F34] FOREIGN KEY([PhoneTypeID])
REFERENCES [CEFTest].[PhoneType] ([PhoneTypeID])
GO

ALTER TABLE [CEFTest].[Phone] CHECK CONSTRAINT [FK__Phone__PhoneType__04115F34]
GO



-- This is how it looks in source and would be executed in target

IF NOT EXISTS (select 0 from sys.columns c join sys.objects o on c.default_object_id=o.[object_id] join sys.objects p on o.parent_object_id=p.[object_id] where p.[object_id]=object_id('[CEFTest].[Person]') and c.[name]='LastUpdatedBy') AND EXISTS (select 0 FROM sys.columns c JOIN sys.objects o ON c.[object_id]=o.[object_id] AND c.[name]='LastUpdatedBy' AND o.[object_id]=object_id('[CEFTest].[Person]'))
	ALTER TABLE [CEFTest].[Person] ADD CONSTRAINT [DF__Person__LastUpda__0505836D] DEFAULT (suser_sname()) FOR [LastUpdatedBy]
GO

-- This is how it looks in source and would be executed in target

IF NOT EXISTS (select 0 from sys.columns c join sys.objects o on c.default_object_id=o.[object_id] join sys.objects p on o.parent_object_id=p.[object_id] where p.[object_id]=object_id('[CEFTest].[Person]') and c.[name]='LastUpdatedDate') AND EXISTS (select 0 FROM sys.columns c JOIN sys.objects o ON c.[object_id]=o.[object_id] AND c.[name]='LastUpdatedDate' AND o.[object_id]=object_id('[CEFTest].[Person]'))
	ALTER TABLE [CEFTest].[Person] ADD CONSTRAINT [DF__Person__LastUpda__05F9A7A6] DEFAULT (sysutcdatetime()) FOR [LastUpdatedDate]
GO

-- This is how it looks in source and would be executed in target

IF NOT EXISTS (select 0 from sys.columns c join sys.objects o on c.default_object_id=o.[object_id] join sys.objects p on o.parent_object_id=p.[object_id] where p.[object_id]=object_id('[CEFTest].[Phone]') and c.[name]='LastUpdatedBy') AND EXISTS (select 0 FROM sys.columns c JOIN sys.objects o ON c.[object_id]=o.[object_id] AND c.[name]='LastUpdatedBy' AND o.[object_id]=object_id('[CEFTest].[Phone]'))
	ALTER TABLE [CEFTest].[Phone] ADD CONSTRAINT [DF__Phone__LastUpdat__0ABE5CC3] DEFAULT (suser_sname()) FOR [LastUpdatedBy]
GO

-- This is how it looks in source and would be executed in target

IF NOT EXISTS (select 0 from sys.columns c join sys.objects o on c.default_object_id=o.[object_id] join sys.objects p on o.parent_object_id=p.[object_id] where p.[object_id]=object_id('[CEFTest].[Phone]') and c.[name]='LastUpdatedDate') AND EXISTS (select 0 FROM sys.columns c JOIN sys.objects o ON c.[object_id]=o.[object_id] AND c.[name]='LastUpdatedDate' AND o.[object_id]=object_id('[CEFTest].[Phone]'))
	ALTER TABLE [CEFTest].[Phone] ADD CONSTRAINT [DF__Phone__LastUpdat__0BB280FC] DEFAULT (sysutcdatetime()) FOR [LastUpdatedDate]
GO

-- This is how it looks in source and would be executed in target

IF NOT EXISTS (select 0 from sys.columns c join sys.objects o on c.default_object_id=o.[object_id] join sys.objects p on o.parent_object_id=p.[object_id] where p.[object_id]=object_id('[CEFTest].[PhoneType]') and c.[name]='LastUpdatedBy') AND EXISTS (select 0 FROM sys.columns c JOIN sys.objects o ON c.[object_id]=o.[object_id] AND c.[name]='LastUpdatedBy' AND o.[object_id]=object_id('[CEFTest].[PhoneType]'))
	ALTER TABLE [CEFTest].[PhoneType] ADD CONSTRAINT [DF__PhoneType__LastU__1CDD0CFE] DEFAULT (suser_sname()) FOR [LastUpdatedBy]
GO

-- This is how it looks in source and would be executed in target

IF NOT EXISTS (select 0 from sys.columns c join sys.objects o on c.default_object_id=o.[object_id] join sys.objects p on o.parent_object_id=p.[object_id] where p.[object_id]=object_id('[CEFTest].[PhoneType]') and c.[name]='LastUpdatedDate') AND EXISTS (select 0 FROM sys.columns c JOIN sys.objects o ON c.[object_id]=o.[object_id] AND c.[name]='LastUpdatedDate' AND o.[object_id]=object_id('[CEFTest].[PhoneType]'))
	ALTER TABLE [CEFTest].[PhoneType] ADD CONSTRAINT [DF__PhoneType__LastU__1DD13137] DEFAULT (sysutcdatetime()) FOR [LastUpdatedDate]
GO

-- This is how it looks in source and would be executed in target
/*    ==Scripting Parameters==

    Source Server Version : SQL Server 2016 (13.0.1742)
    Source Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Source Database Engine Type : Standalone SQL Server

    Target Server Version : SQL Server 2016
    Target Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Target Database Engine Type : Standalone SQL Server
*/

ALTER TABLE [CEFTest].[Phone]  WITH NOCHECK ADD  CONSTRAINT [FK__Phone__PersonID__32CC4E1D] FOREIGN KEY([PersonID])
REFERENCES [CEFTest].[Person] ([PersonID])
GO

ALTER TABLE [CEFTest].[Phone] CHECK CONSTRAINT [FK__Phone__PersonID__32CC4E1D]
GO



-- This is how it looks in source and would be executed in target

IF NOT EXISTS (select 0 from sys.columns c join sys.objects o on c.default_object_id=o.[object_id] join sys.objects p on o.parent_object_id=p.[object_id] where p.[object_id]=object_id('[CEFTest].[Person]') and c.[name]='IsDeleted') AND EXISTS (select 0 FROM sys.columns c JOIN sys.objects o ON c.[object_id]=o.[object_id] AND c.[name]='IsDeleted' AND o.[object_id]=object_id('[CEFTest].[Person]'))
	ALTER TABLE [CEFTest].[Person] ADD CONSTRAINT [DF__Person__IsDelete__420E91AD] DEFAULT ((0)) FOR [IsDeleted]
GO

-- This is how it looks in source and would be executed in target
/*    ==Scripting Parameters==

    Source Server Version : SQL Server 2016 (13.0.1742)
    Source Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Source Database Engine Type : Standalone SQL Server

    Target Server Version : SQL Server 2016
    Target Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Target Database Engine Type : Standalone SQL Server
*/

ALTER TABLE [CEFTest].[Person]  WITH NOCHECK ADD  CONSTRAINT [FK__Person__ParentPe__7F4CAA17] FOREIGN KEY([ParentPersonID])
REFERENCES [CEFTest].[Person] ([PersonID])
GO

ALTER TABLE [CEFTest].[Person] CHECK CONSTRAINT [FK__Person__ParentPe__7F4CAA17]
GO



-- This is how it looks in source and would be executed in target
/*    ==Scripting Parameters==

    Source Server Version : SQL Server 2016 (13.0.1742)
    Source Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Source Database Engine Type : Standalone SQL Server

    Target Server Version : SQL Server 2016
    Target Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Target Database Engine Type : Standalone SQL Server
*/

/****** Object:  StoredProcedure [CEFTest].[up_PhoneType_d]    Script Date: 12/19/2017 5:45:55 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE PROCEDURE CEFTest.[up_PhoneType_d]
    @RetVal int = NULL OUTPUT,
    @Msg varchar(200) = NULL OUTPUT,
    @PhoneTypeID int
    , @LastUpdatedBy varchar(50) = NULL
    
AS
/***********************************************************
Name:  CEFTest.up_PhoneType_d
Date: 12/6/2017 9:35 PM
Author: System-generated
Description: Standard delete procedure for PhoneType

<UDP><Name>SystemGenerated</Name><Value>True</Value></UDP>

Log:         
**********************************************************/
BEGIN

SET NOCOUNT ON
DECLARE @__e int
DECLARE @__r int
SET @__e = 0
SET @RetVal = 1
SET @Msg = ''


DELETE [CEFTest].[PhoneType]
WHERE
	PhoneTypeID = @PhoneTypeID

SELECT @__e = @@ERROR, @__r = @@ROWCOUNT


RETURN @__r
END
GO


-- This is how it looks in source and would be executed in target
/*    ==Scripting Parameters==

    Source Server Version : SQL Server 2016 (13.0.1742)
    Source Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Source Database Engine Type : Standalone SQL Server

    Target Server Version : SQL Server 2016
    Target Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Target Database Engine Type : Standalone SQL Server
*/

/****** Object:  StoredProcedure [CEFTest].[up_PhoneType_u]    Script Date: 12/19/2017 5:45:55 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE PROCEDURE CEFTest.[up_PhoneType_u]
	@RetVal int = NULL OUTPUT,
	@Msg varchar(200) = NULL OUTPUT,
	@PhoneTypeID int
	, @PhoneTypeDesc varchar(100)
	, @LastUpdatedBy varchar(50)
	, @LastUpdatedDate datetime2 = NULL OUTPUT
AS
/***********************************************************
Name:  CEFTest.up_PhoneType_u    
Date: 12/6/2017 9:35 PM
Author: System-generated
Description: Standard update procedure for PhoneType

<UDP><Name>SystemGenerated</Name><Value>True</Value></UDP>

Log:         
**********************************************************/
BEGIN

SET NOCOUNT ON
DECLARE @__e int
DECLARE @__r int
SET @__e = 0
SET @RetVal = 1
SET @Msg = ''

DECLARE @NewLastUpdatedDate datetime2
SET @NewLastUpdatedDate = SYSUTCDATETIME()


UPDATE [CEFTest].[PhoneType]
SET
	PhoneTypeDesc = @PhoneTypeDesc
	, LastUpdatedBy = @LastUpdatedBy
	, LastUpdatedDate = @NewLastUpdatedDate
WHERE
	CEFTest.PhoneType.PhoneTypeID = @PhoneTypeID


SELECT @__e = @@ERROR, @__r = @@ROWCOUNT



-- We will return new LastUpdatedDate
SET @LastUpdatedDate = @NewLastUpdatedDate

RETURN @__r
END
GO


-- This is how it looks in source and would be executed in target
/*    ==Scripting Parameters==

    Source Server Version : SQL Server 2016 (13.0.1742)
    Source Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Source Database Engine Type : Standalone SQL Server

    Target Server Version : SQL Server 2016
    Target Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Target Database Engine Type : Standalone SQL Server
*/

/****** Object:  StoredProcedure [CEFTest].[up_PhoneType_i]    Script Date: 12/19/2017 5:45:55 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE PROCEDURE CEFTest.[up_PhoneType_i]
	@RetVal int = NULL OUTPUT,
	@Msg varchar(200) = NULL OUTPUT,
	@PhoneTypeID int = NULL OUTPUT
	, @PhoneTypeDesc varchar(100) 
	, @LastUpdatedBy varchar(50)
	, @LastUpdatedDate datetime2 = NULL OUTPUT
AS
/***********************************************************
Name:  CEFTest.up_PhoneType_i
Date: 12/6/2017 9:35 PM
Author: System-generated
Description: Standard insert procedure for PhoneType

<UDP><Name>SystemGenerated</Name><Value>True</Value></UDP>

Log:         
**********************************************************/
BEGIN

SET NOCOUNT ON
DECLARE @__e int
SET @__e = 0
SET @RetVal = 1
SET @Msg = ''

SET @LastUpdatedDate = SYSUTCDATETIME()

INSERT INTO [CEFTest].[PhoneType] (
	PhoneTypeDesc
	, LastUpdatedBy
	, LastUpdatedDate )
VALUES (
	@PhoneTypeDesc
	, @LastUpdatedBy
	, @LastUpdatedDate )

SET @PhoneTypeID = SCOPE_IDENTITY()
RETURN 1
END
GO


-- This is how it looks in source and would be executed in target
/*    ==Scripting Parameters==

    Source Server Version : SQL Server 2016 (13.0.1742)
    Source Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Source Database Engine Type : Standalone SQL Server

    Target Server Version : SQL Server 2016
    Target Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Target Database Engine Type : Standalone SQL Server
*/

/****** Object:  StoredProcedure [CEFTest].[up_PhoneType_ByKey]    Script Date: 12/19/2017 5:45:55 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE PROCEDURE CEFTest.[up_PhoneType_ByKey]
	@RetVal int = NULL OUTPUT,
	@Msg varchar(200) = NULL OUTPUT,
	@PhoneTypeID int = NULL
AS
/***********************************************************
Name:  CEFTest.up_PhoneType_ByKey
Date: 12/6/2017 9:35 PM
Author: System-generated
Description: Selects specific record from PhoneType, by key

<UDP><Name>SystemGenerated</Name><Value>True</Value></UDP>
<UDP><Name>NullRunParameters</Name><Value>NULL,NULL,NULL</Value></UDP>

Log:         
**********************************************************/
BEGIN

DECLARE @__e int
SET @__e = 0
SET @RetVal = 1
SET @Msg = ''

SELECT
	PhoneTypeID
	, PhoneTypeDesc
	, LastUpdatedBy
	, LastUpdatedDate
FROM
	[CEFTest].[PhoneType]
WHERE
	PhoneTypeID = @PhoneTypeID

SELECT @__e = @@ERROR

IF @__e <> 0
BEGIN
    IF @RetVal = 1
    BEGIN
        SET @RetVal = 3
        SET @Msg = 'PhoneType retrieve by key failed with code ' + CONVERT(varchar, @__e)
    END
END

END
GO


-- This is how it looks in source and would be executed in target
/*    ==Scripting Parameters==

    Source Server Version : SQL Server 2016 (13.0.1742)
    Source Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Source Database Engine Type : Standalone SQL Server

    Target Server Version : SQL Server 2016
    Target Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Target Database Engine Type : Standalone SQL Server
*/

/****** Object:  StoredProcedure [CEFTest].[up_PhoneType_ForList]    Script Date: 12/19/2017 5:45:55 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE PROCEDURE CEFTest.[up_PhoneType_ForList]
	@RetVal int = NULL OUTPUT,
	@Msg varchar(200) = NULL OUTPUT
AS
/***********************************************************
Name:  CEFTest.up_PhoneType_ForList
Date: 12/6/2017 9:35 PM
Author: System-generated
Description: Standard select all for list procedure for PhoneType

<UDP><Name>SystemGenerated</Name><Value>True</Value></UDP>
<UDP><Name>NullRunParameters</Name><Value>NULL,NULL</Value></UDP>
<UDP><Name>ProcedureWrapperClassName</Name><Value>PhoneType</Value></UDP>
<UDP><Name>ProcedureWrapperMethodName</Name><Value>RetrieveAll</Value></UDP>

Log:         
**********************************************************/
BEGIN

SELECT
	PhoneTypeID
	, PhoneTypeDesc
	, LastUpdatedBy
	, LastUpdatedDate
FROM
	[CEFTest].[PhoneType]


END
GO


-- This is how it looks in source and would be executed in target
/*    ==Scripting Parameters==

    Source Server Version : SQL Server 2016 (13.0.1742)
    Source Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Source Database Engine Type : Standalone SQL Server

    Target Server Version : SQL Server 2016
    Target Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Target Database Engine Type : Standalone SQL Server
*/

/****** Object:  StoredProcedure [CEFTest].[up_Person_ByParentPersonID]    Script Date: 12/19/2017 5:45:55 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE PROCEDURE CEFTest.[up_Person_ByParentPersonID]
	@RetVal int = NULL OUTPUT,
	@Msg varchar(200) = NULL OUTPUT,
	@ParentPersonID int
AS
BEGIN

DECLARE @__e int
SET @__e = 0
SET @RetVal = 1
SET @Msg = ''

SELECT
	PersonID
	, Name
	, Age
	, ParentPersonID
	, Gender
	, LastUpdatedBy
	, LastUpdatedDate
FROM
	[CEFTest].[Person]
WHERE
	ParentPersonID = @ParentPersonID

SELECT @__e = @@ERROR

IF @__e <> 0
BEGIN
    IF @RetVal = 1
    BEGIN
        SET @RetVal = 3
        SET @Msg = 'Person retrieve by key failed with code ' + CONVERT(varchar, @__e)
    END
END

END
GO


-- This is how it looks in source and would be executed in target
/*    ==Scripting Parameters==

    Source Server Version : SQL Server 2016 (13.0.1742)
    Source Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Source Database Engine Type : Standalone SQL Server

    Target Server Version : SQL Server 2016
    Target Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Target Database Engine Type : Standalone SQL Server
*/

/****** Object:  StoredProcedure [CEFTest].[up_Phone_ByPersonID]    Script Date: 12/19/2017 5:45:55 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE PROCEDURE CEFTest.[up_Phone_ByPersonID]
	@RetVal int = NULL OUTPUT,
	@Msg varchar(200) = NULL OUTPUT,
	@PersonID int 
AS
/***********************************************************
Name:  CEFTest.up_Phone_ByKey
Date: 12/8/2017 12:47 PM
Author: System-generated
Description: Selects specific record from Phone, by key

<UDP><Name>SystemGenerated</Name><Value>True</Value></UDP>
<UDP><Name>NullRunParameters</Name><Value>NULL,NULL,NULL</Value></UDP>

Log:         
**********************************************************/
BEGIN

DECLARE @__e int
SET @__e = 0
SET @RetVal = 1
SET @Msg = ''

SELECT
	PhoneID
	, PhoneTypeID
	, Number
	, PersonID
	, LastUpdatedBy
	, LastUpdatedDate
FROM
	[CEFTest].[Phone]
WHERE
	PersonID = @PersonID

SELECT @__e = @@ERROR

IF @__e <> 0
BEGIN
    IF @RetVal = 1
    BEGIN
        SET @RetVal = 3
        SET @Msg = 'Phone retrieve by key failed with code ' + CONVERT(varchar, @__e)
    END
END

END
GO


-- This is how it looks in source and would be executed in target
/*    ==Scripting Parameters==

    Source Server Version : SQL Server 2016 (13.0.1742)
    Source Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Source Database Engine Type : Standalone SQL Server

    Target Server Version : SQL Server 2016
    Target Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Target Database Engine Type : Standalone SQL Server
*/

/****** Object:  StoredProcedure [CEFTest].[up_Phone_d]    Script Date: 12/19/2017 5:45:55 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE PROCEDURE CEFTest.[up_Phone_d]
    @RetVal int = NULL OUTPUT,
    @Msg varchar(200) = NULL OUTPUT,
    @PhoneID int
    , @LastUpdatedBy varchar(50) = NULL
    
AS
/***********************************************************
Name:  CEFTest.up_Phone_d
Date: 12/18/2017 7:30 PM
Author: System-generated
Description: Standard delete procedure for Phone

<UDP><Name>SystemGenerated</Name><Value>True</Value></UDP>

Log:         
**********************************************************/
BEGIN

SET NOCOUNT ON
DECLARE @__e int
DECLARE @__r int
SET @__e = 0
SET @RetVal = 1
SET @Msg = ''


DELETE [CEFTest].[Phone]
WHERE
	PhoneID = @PhoneID

SELECT @__e = @@ERROR, @__r = @@ROWCOUNT

IF @RetVal = 1 AND @__r = 0
BEGIN
	SET @RetVal = 2
	SET @Msg = 'Warning:  The record has been updated or deleted by another connection after this particular record was originally retrieved in the current session.  (Table is Phone.)'
END


RETURN @__r
END
GO


-- This is how it looks in source and would be executed in target
/*    ==Scripting Parameters==

    Source Server Version : SQL Server 2016 (13.0.1742)
    Source Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Source Database Engine Type : Standalone SQL Server

    Target Server Version : SQL Server 2016
    Target Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Target Database Engine Type : Standalone SQL Server
*/

/****** Object:  StoredProcedure [CEFTest].[up_Phone_u]    Script Date: 12/19/2017 5:45:56 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE PROCEDURE CEFTest.[up_Phone_u]
	@RetVal int = NULL OUTPUT,
	@Msg varchar(200) = NULL OUTPUT,
	@PhoneID int
	, @PhoneTypeID int
	, @Number varchar(20)
	, @PersonID int
	, @LastUpdatedBy varchar(50)
	, @LastUpdatedDate datetime2 = NULL OUTPUT
AS
/***********************************************************
Name:  CEFTest.up_Phone_u    
Date: 12/18/2017 7:30 PM
Author: System-generated
Description: Standard update procedure for Phone

<UDP><Name>SystemGenerated</Name><Value>True</Value></UDP>

Log:         
**********************************************************/
BEGIN

SET NOCOUNT ON
DECLARE @__e int
DECLARE @__r int
SET @__e = 0
SET @RetVal = 1
SET @Msg = ''

DECLARE @NewLastUpdatedDate datetime2
SET @NewLastUpdatedDate = SYSUTCDATETIME()


IF @LastUpdatedDate IS NOT NULL AND NOT EXISTS
	(SELECT 0
	FROM [CEFTest].[Phone]
	WHERE CEFTest.Phone.PhoneID = @PhoneID
	AND LastUpdatedDate = @LastUpdatedDate)
BEGIN
	SET @RetVal = 2
	SET @Msg = 'Warning:  The record has been updated or deleted by another connection after this particular record was originally retrieved in the current session.  (Table is Phone.)'
END

UPDATE [CEFTest].[Phone]
SET
	PhoneTypeID = @PhoneTypeID
	, Number = @Number
	, PersonID = @PersonID
	, LastUpdatedBy = @LastUpdatedBy
	, LastUpdatedDate = @NewLastUpdatedDate
WHERE
	CEFTest.Phone.PhoneID = @PhoneID


SELECT @__e = @@ERROR, @__r = @@ROWCOUNT


IF @RetVal = 1 AND @__r = 0
BEGIN
	SET @RetVal = 2
	SET @Msg = 'Warning:  The record has been updated or deleted by another connection after this particular record was originally retrieved in the current session.  (Table is Phone.)'
END


-- We will return new LastUpdatedDate
SET @LastUpdatedDate = @NewLastUpdatedDate

RETURN @__r
END
GO


-- This is how it looks in source and would be executed in target
/*    ==Scripting Parameters==

    Source Server Version : SQL Server 2016 (13.0.1742)
    Source Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Source Database Engine Type : Standalone SQL Server

    Target Server Version : SQL Server 2016
    Target Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Target Database Engine Type : Standalone SQL Server
*/

/****** Object:  StoredProcedure [CEFTest].[up_Phone_i]    Script Date: 12/19/2017 5:45:55 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE PROCEDURE CEFTest.[up_Phone_i]
	@RetVal int = NULL OUTPUT,
	@Msg varchar(200) = NULL OUTPUT,
	@PhoneID int = NULL OUTPUT
	, @PhoneTypeID int 
	, @Number varchar(20) 
	, @PersonID int = NULL
	, @LastUpdatedBy varchar(50)
	, @LastUpdatedDate datetime2 = NULL OUTPUT
AS
/***********************************************************
Name:  CEFTest.up_Phone_i
Date: 12/18/2017 7:30 PM
Author: System-generated
Description: Standard insert procedure for Phone

<UDP><Name>SystemGenerated</Name><Value>True</Value></UDP>

Log:         
**********************************************************/
BEGIN

SET NOCOUNT ON
DECLARE @__e int
SET @__e = 0
SET @RetVal = 1
SET @Msg = ''

SET @LastUpdatedDate = SYSUTCDATETIME()

INSERT INTO [CEFTest].[Phone] (
	PhoneTypeID
	, Number
	, PersonID
	, LastUpdatedBy
	, LastUpdatedDate )
VALUES (
	@PhoneTypeID
	, @Number
	, @PersonID
	, @LastUpdatedBy
	, @LastUpdatedDate )

SET @PhoneID = SCOPE_IDENTITY()
RETURN 1
END
GO


-- This is how it looks in source and would be executed in target
/*    ==Scripting Parameters==

    Source Server Version : SQL Server 2016 (13.0.1742)
    Source Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Source Database Engine Type : Standalone SQL Server

    Target Server Version : SQL Server 2016
    Target Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Target Database Engine Type : Standalone SQL Server
*/

/****** Object:  StoredProcedure [CEFTest].[up_Phone_ByKey]    Script Date: 12/19/2017 5:45:56 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE PROCEDURE CEFTest.[up_Phone_ByKey]
	@RetVal int = NULL OUTPUT,
	@Msg varchar(200) = NULL OUTPUT,
	@PhoneID int = NULL
AS
/***********************************************************
Name:  CEFTest.up_Phone_ByKey
Date: 12/18/2017 7:30 PM
Author: System-generated
Description: Selects specific record from Phone, by key

<UDP><Name>SystemGenerated</Name><Value>True</Value></UDP>
<UDP><Name>NullRunParameters</Name><Value>NULL,NULL,NULL</Value></UDP>

Log:         
**********************************************************/
BEGIN

DECLARE @__e int
SET @__e = 0
SET @RetVal = 1
SET @Msg = ''

SELECT
	PhoneID
	, PhoneTypeID
	, Number
	, PersonID
	, LastUpdatedBy
	, LastUpdatedDate
FROM
	[CEFTest].[Phone]
WHERE
	PhoneID = @PhoneID

SELECT @__e = @@ERROR

IF @__e <> 0
BEGIN
    IF @RetVal = 1
    BEGIN
        SET @RetVal = 3
        SET @Msg = 'Phone retrieve by key failed with code ' + CONVERT(varchar, @__e)
    END
END

END
GO


-- This is how it looks in source and would be executed in target
/*    ==Scripting Parameters==

    Source Server Version : SQL Server 2016 (13.0.1742)
    Source Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Source Database Engine Type : Standalone SQL Server

    Target Server Version : SQL Server 2016
    Target Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Target Database Engine Type : Standalone SQL Server
*/

/****** Object:  StoredProcedure [CEFTest].[up_Phone_ForList]    Script Date: 12/19/2017 5:45:57 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE PROCEDURE CEFTest.[up_Phone_ForList]
	@RetVal int = NULL OUTPUT,
	@Msg varchar(200) = NULL OUTPUT
AS
/***********************************************************
Name:  CEFTest.up_Phone_ForList
Date: 12/18/2017 7:30 PM
Author: System-generated
Description: Standard select all for list procedure for Phone

<UDP><Name>SystemGenerated</Name><Value>True</Value></UDP>
<UDP><Name>NullRunParameters</Name><Value>NULL,NULL</Value></UDP>
<UDP><Name>ProcedureWrapperClassName</Name><Value>Phone</Value></UDP>
<UDP><Name>ProcedureWrapperMethodName</Name><Value>RetrieveAll</Value></UDP>

Log:         
**********************************************************/
BEGIN

SELECT
	PhoneID
	, PhoneTypeID
	, Number
	, PersonID
	, LastUpdatedBy
	, LastUpdatedDate
FROM
	[CEFTest].[Phone]


END
GO


-- This is how it looks in source and would be executed in target
/*    ==Scripting Parameters==

    Source Server Version : SQL Server 2016 (13.0.1742)
    Source Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Source Database Engine Type : Standalone SQL Server

    Target Server Version : SQL Server 2016
    Target Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Target Database Engine Type : Standalone SQL Server
*/

/****** Object:  StoredProcedure [CEFTest].[up_Person_d]    Script Date: 12/19/2017 5:45:57 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE PROCEDURE CEFTest.[up_Person_d]
    @RetVal int = NULL OUTPUT,
    @Msg varchar(200) = NULL OUTPUT,
    @PersonID int
    , @LastUpdatedBy varchar(50) = NULL
    , @LastUpdatedDate datetime2 = NULL
AS
/***********************************************************
Name:  CEFTest.up_Person_d
Date: 12/18/2017 7:30 PM
Author: System-generated
Description: Standard delete procedure for Person

<UDP><Name>SystemGenerated</Name><Value>True</Value></UDP>

Log:         
**********************************************************/
BEGIN

SET NOCOUNT ON
DECLARE @__e int
DECLARE @__r int
SET @__e = 0
SET @RetVal = 1
SET @Msg = ''



UPDATE [CEFTest].[Person]
SET
	IsDeleted = 1,
	LastUpdatedBy = @LastUpdatedBy,
	LastUpdatedDate = SYSUTCDATETIME()
WHERE
	CEFTest.Person.PersonID = @PersonID
AND LastUpdatedDate = @LastUpdatedDate

SELECT @__e = @@ERROR, @__r = @@ROWCOUNT

IF @__r = 0
BEGIN
	SET @Msg = 'Database error - row in Person was updated or deleted by someone else prior to saving - cannot delete row.'
	RAISERROR (@Msg, 16, 1)
END


RETURN @__r
END
GO


-- This is how it looks in source and would be executed in target
/*    ==Scripting Parameters==

    Source Server Version : SQL Server 2016 (13.0.1742)
    Source Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Source Database Engine Type : Standalone SQL Server

    Target Server Version : SQL Server 2016
    Target Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Target Database Engine Type : Standalone SQL Server
*/

/****** Object:  StoredProcedure [CEFTest].[up_Person_u]    Script Date: 12/19/2017 5:45:56 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE PROCEDURE CEFTest.[up_Person_u]
	@RetVal int = NULL OUTPUT,
	@Msg varchar(200) = NULL OUTPUT,
	@PersonID int
	, @Name varchar(100)
	, @Age int
	, @ParentPersonID int
	, @Gender char(1)
	, @LastUpdatedBy varchar(50)
	, @LastUpdatedDate datetime2 = NULL OUTPUT
AS
/***********************************************************
Name:  CEFTest.up_Person_u    
Date: 12/18/2017 7:30 PM
Author: System-generated
Description: Standard update procedure for Person

<UDP><Name>SystemGenerated</Name><Value>True</Value></UDP>

Log:         
**********************************************************/
BEGIN

SET NOCOUNT ON
DECLARE @__e int
DECLARE @__r int
SET @__e = 0
SET @RetVal = 1
SET @Msg = ''

DECLARE @NewLastUpdatedDate datetime2
SET @NewLastUpdatedDate = SYSUTCDATETIME()


UPDATE [CEFTest].[Person]
SET
	Name = @Name
	, Age = @Age
	, ParentPersonID = @ParentPersonID
	, Gender = @Gender
	, LastUpdatedBy = @LastUpdatedBy
	, LastUpdatedDate = @NewLastUpdatedDate
WHERE
	CEFTest.Person.PersonID = @PersonID
-- Concurrency checking
AND	[CEFTest].[Person].LastUpdatedDate = @LastUpdatedDate


SELECT @__e = @@ERROR, @__r = @@ROWCOUNT


IF @__r = 0
BEGIN
    SET @Msg = 'Database error - row in Person was updated or deleted by someone else prior to saving - cannot update row.'
    RAISERROR (@Msg, 16, 1)
END

-- We will return new LastUpdatedDate
SET @LastUpdatedDate = @NewLastUpdatedDate

RETURN @__r
END
GO


-- This is how it looks in source and would be executed in target
/*    ==Scripting Parameters==

    Source Server Version : SQL Server 2016 (13.0.1742)
    Source Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Source Database Engine Type : Standalone SQL Server

    Target Server Version : SQL Server 2016
    Target Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Target Database Engine Type : Standalone SQL Server
*/

/****** Object:  StoredProcedure [CEFTest].[up_Person_i]    Script Date: 12/19/2017 5:45:56 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE PROCEDURE CEFTest.[up_Person_i]
	@RetVal int = NULL OUTPUT,
	@Msg varchar(200) = NULL OUTPUT,
	@PersonID int = NULL OUTPUT
	, @Name varchar(100) 
	, @Age int 
	, @ParentPersonID int = NULL
	, @Gender char(1) = NULL
	, @LastUpdatedBy varchar(50)
	, @LastUpdatedDate datetime2 = NULL OUTPUT
AS
/***********************************************************
Name:  CEFTest.up_Person_i
Date: 12/18/2017 7:30 PM
Author: System-generated
Description: Standard insert procedure for Person

<UDP><Name>SystemGenerated</Name><Value>True</Value></UDP>

Log:         
**********************************************************/
BEGIN

SET NOCOUNT ON
DECLARE @__e int
SET @__e = 0
SET @RetVal = 1
SET @Msg = ''

SET @LastUpdatedDate = SYSUTCDATETIME()

INSERT INTO [CEFTest].[Person] (
	Name
	, Age
	, ParentPersonID
	, Gender
	, LastUpdatedBy
	, LastUpdatedDate )
VALUES (
	@Name
	, @Age
	, @ParentPersonID
	, @Gender
	, @LastUpdatedBy
	, @LastUpdatedDate )

SET @PersonID = SCOPE_IDENTITY()
RETURN 1
END
GO


-- This is how it looks in source and would be executed in target
/*    ==Scripting Parameters==

    Source Server Version : SQL Server 2016 (13.0.1742)
    Source Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Source Database Engine Type : Standalone SQL Server

    Target Server Version : SQL Server 2016
    Target Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Target Database Engine Type : Standalone SQL Server
*/

/****** Object:  StoredProcedure [CEFTest].[up_Person_ByKey]    Script Date: 12/19/2017 5:45:56 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE PROCEDURE CEFTest.[up_Person_ByKey]
	@RetVal int = NULL OUTPUT,
	@Msg varchar(200) = NULL OUTPUT,
	@PersonID int = NULL
AS
/***********************************************************
Name:  CEFTest.up_Person_ByKey
Date: 12/18/2017 7:30 PM
Author: System-generated
Description: Selects specific record from Person, by key

<UDP><Name>SystemGenerated</Name><Value>True</Value></UDP>
<UDP><Name>NullRunParameters</Name><Value>NULL,NULL,NULL</Value></UDP>

Log:         
**********************************************************/
BEGIN

DECLARE @__e int
SET @__e = 0
SET @RetVal = 1
SET @Msg = ''

SELECT
	PersonID
	, Name
	, Age
	, ParentPersonID
	, Gender
	, LastUpdatedBy
	, LastUpdatedDate
FROM
	[CEFTest].[Person]
WHERE
	PersonID = @PersonID

SELECT @__e = @@ERROR

IF @__e <> 0
BEGIN
    IF @RetVal = 1
    BEGIN
        SET @RetVal = 3
        SET @Msg = 'Person retrieve by key failed with code ' + CONVERT(varchar, @__e)
    END
END

END
GO


-- This is how it looks in source and would be executed in target
/*    ==Scripting Parameters==

    Source Server Version : SQL Server 2016 (13.0.1742)
    Source Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Source Database Engine Type : Standalone SQL Server

    Target Server Version : SQL Server 2016
    Target Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Target Database Engine Type : Standalone SQL Server
*/

/****** Object:  StoredProcedure [CEFTest].[up_Person_ForList]    Script Date: 12/19/2017 5:45:56 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE PROCEDURE CEFTest.[up_Person_ForList]
	@RetVal int = NULL OUTPUT,
	@Msg varchar(200) = NULL OUTPUT
AS
/***********************************************************
Name:  CEFTest.up_Person_ForList
Date: 12/18/2017 7:30 PM
Author: System-generated
Description: Standard select all for list procedure for Person

<UDP><Name>SystemGenerated</Name><Value>True</Value></UDP>
<UDP><Name>NullRunParameters</Name><Value>NULL,NULL</Value></UDP>
<UDP><Name>ProcedureWrapperClassName</Name><Value>Person</Value></UDP>
<UDP><Name>ProcedureWrapperMethodName</Name><Value>RetrieveAll</Value></UDP>

Log:         
**********************************************************/
BEGIN

SELECT
	PersonID
	, Name
	, Age
	, ParentPersonID
	, Gender
	, LastUpdatedBy
	, LastUpdatedDate
FROM
	[CEFTest].[Person]


END
GO


-- This is how it looks in source and would be executed in target
/*    ==Scripting Parameters==

    Source Server Version : SQL Server 2016 (13.0.1742)
    Source Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Source Database Engine Type : Standalone SQL Server

    Target Server Version : SQL Server 2016
    Target Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Target Database Engine Type : Standalone SQL Server
*/

/****** Object:  StoredProcedure [CEFTest].[up_Phone_AllForFamily]    Script Date: 12/19/2017 5:45:57 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE PROCEDURE CEFTest.[up_Phone_AllForFamily]
	@RetVal int = NULL OUTPUT,
	@Msg varchar(200) = NULL OUTPUT,
	@PersonID int 
AS
/***********************************************************
Name:  CEFTest.up_Phone_ByKey
Date: 12/8/2017 12:47 PM
Author: System-generated
Description: Selects specific record from Phone, by key

<UDP><Name>SystemGenerated</Name><Value>True</Value></UDP>
<UDP><Name>NullRunParameters</Name><Value>NULL,NULL,NULL</Value></UDP>

Log:         
**********************************************************/
BEGIN

DECLARE @__e int
SET @__e = 0
SET @RetVal = 1
SET @Msg = ''

SELECT
	PhoneID
	, PhoneTypeID
	, Number
	, PersonID
	, LastUpdatedBy
	, LastUpdatedDate
FROM
	[CEFTest].[Phone]
WHERE
	PersonID = @PersonID
UNION ALL
SELECT
	PhoneID
	, PhoneTypeID
	, Number
	, h.PersonID
	, h.LastUpdatedBy
	, h.LastUpdatedDate
FROM
	[CEFTest].[Person] p
	JOIN [CEFTest].[Phone] h
		ON p.PersonID = h.PersonID
WHERE
	p.ParentPersonID = @PersonID;

SELECT @__e = @@ERROR

IF @__e <> 0
BEGIN
    IF @RetVal = 1
    BEGIN
        SET @RetVal = 3
        SET @Msg = 'Phone retrieve by key failed with code ' + CONVERT(varchar, @__e)
    END
END

END
GO


-- This is how it looks in source and would be executed in target
/*    ==Scripting Parameters==

    Source Server Version : SQL Server 2016 (13.0.1742)
    Source Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Source Database Engine Type : Standalone SQL Server

    Target Server Version : SQL Server 2016
    Target Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Target Database Engine Type : Standalone SQL Server
*/

/****** Object:  StoredProcedure [CEFTest].[up_Person_SummaryForParents]    Script Date: 12/19/2017 5:45:56 PM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE PROCEDURE CEFTest.[up_Person_SummaryForParents]
	@RetVal int = NULL OUTPUT,
	@Msg varchar(200) = NULL OUTPUT,
	@MinimumAge int = NULL
AS
BEGIN

DECLARE @__e int
SET @__e = 0
SET @RetVal = 1
SET @Msg = ''

SELECT
	PersonID
	, [Name]
	, Age
	, ParentPersonID
	, Gender
	, LastUpdatedBy
	, LastUpdatedDate
	, (SELECT COUNT(*)
		FROM CEFTest.Person c
		WHERE c.ParentPersonID = p.PersonID
		AND c.Gender = 'M') AS MaleChildren
	, (SELECT COUNT(*)
		FROM CEFTest.Person c
		WHERE c.ParentPersonID = p.PersonID
		AND c.Gender = 'F') AS FemaleChildren
	, (SELECT COUNT(*)
		FROM CEFTest.Phone ph
		WHERE p.PersonID = ph.PersonID) +
	  (SELECT COUNT(*)
	  	FROM CEFTest.Person c
	  		JOIN CEFTest.Phone cph
	  			ON c.PersonID = cph.PersonID
	  	WHERE c.ParentPersonID = p.PersonID) AS FamilyPhones
FROM
	[CEFTest].[Person] p
WHERE
	(@MinimumAge IS NULL OR p.Age >= @MinimumAge)
AND	EXISTS
	(SELECT 0
	FROM CEFTest.Person c
	WHERE p.PersonID = c.ParentPersonID)
ORDER BY
	(SELECT COUNT(*)
	FROM CEFTest.Person c
	WHERE c.ParentPersonID = p.PersonID) DESC, Age DESC;

SELECT @__e = @@ERROR

IF @__e <> 0
BEGIN
    IF @RetVal = 1
    BEGIN
        SET @RetVal = 3
        SET @Msg = 'Person retrieve by key failed with code ' + CONVERT(varchar, @__e)
    END
END

END
GO

/******************************
	Reference Data
******************************/

SET IDENTITY_INSERT CEFTest.PhoneType ON
GO

INSERT [CEFTest].[PhoneType] (
	PhoneTypeID
	, [PhoneTypeDesc]
	, [LastUpdatedBy]
	, [LastUpdatedDate] )
VALUES (
	1,
	'Home'
	, 'demo'
	, GETUTCDATE() );

INSERT [CEFTest].[PhoneType] (
	PhoneTypeID
	, [PhoneTypeDesc]
	, [LastUpdatedBy]
	, [LastUpdatedDate] )
VALUES (
	2,
	'Work'
	, 'demo'
	, GETUTCDATE() );

INSERT [CEFTest].[PhoneType] (
	PhoneTypeID
	, [PhoneTypeDesc]
	, [LastUpdatedBy]
	, [LastUpdatedDate] )
VALUES (
	3,
	'Mobile'
	, 'demo'
	, GETUTCDATE() );
GO

SET IDENTITY_INSERT CEFTest.PhoneType OFF
GO

