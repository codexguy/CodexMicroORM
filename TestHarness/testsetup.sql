DELETE CEFTest.Phone;
DELETE CEFTest.Person;
GO

/****** Object:  Schema [CEFTest]    Script Date: 12/6/2017 8:39:09 AM ******/
CREATE SCHEMA [CEFTest]
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

CREATE TABLE [CEFTest].[PhoneType](
	[PhoneTypeID] [int] IDENTITY(1,1) NOT NULL,
	[PhoneTypeDesc] [varchar](100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL
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

CREATE TABLE [CEFTest].[Person](
	[PersonID] [int] IDENTITY(1,1) NOT NULL,
	[Name] [varchar](100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[Age] [int] NOT NULL,
	[ParentPersonID] [int] NULL,
	[LastUpdatedBy] [varchar](50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	[LastUpdatedDate] [datetime2](7) NOT NULL
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

/****** Object:  Index [PK__PhoneTyp__F39F5BB9543FAE7C]    Script Date: 12/6/2017 8:39:10 AM ******/
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[CEFTest].[PhoneType]') AND name = N'PK__PhoneTyp__F39F5BB9543FAE7C')
ALTER TABLE [CEFTest].[PhoneType] ADD  CONSTRAINT [PK__PhoneTyp__F39F5BB9543FAE7C] PRIMARY KEY CLUSTERED 
(
	[PhoneTypeID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
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

/****** Object:  Index [PK__Phone__F3EE4BD0FBB96228]    Script Date: 12/6/2017 8:39:10 AM ******/
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[CEFTest].[Phone]') AND name = N'PK__Phone__F3EE4BD0FBB96228')
ALTER TABLE [CEFTest].[Phone] ADD  CONSTRAINT [PK__Phone__F3EE4BD0FBB96228] PRIMARY KEY CLUSTERED 
(
	[PhoneID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
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

/****** Object:  Index [PK__Person__AA2FFB855E4FB0EC]    Script Date: 12/6/2017 8:39:10 AM ******/
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[CEFTest].[Person]') AND name = N'PK__Person__AA2FFB855E4FB0EC')
ALTER TABLE [CEFTest].[Person] ADD  CONSTRAINT [PK__Person__AA2FFB855E4FB0EC] PRIMARY KEY CLUSTERED 
(
	[PersonID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
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

ALTER TABLE [CEFTest].[Phone]  WITH CHECK ADD  CONSTRAINT [FK__Phone__PhoneType__04115F34] FOREIGN KEY([PhoneTypeID])
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
/*    ==Scripting Parameters==

    Source Server Version : SQL Server 2016 (13.0.1742)
    Source Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Source Database Engine Type : Standalone SQL Server

    Target Server Version : SQL Server 2016
    Target Database Engine Edition : Microsoft SQL Server Enterprise Edition
    Target Database Engine Type : Standalone SQL Server
*/

ALTER TABLE [CEFTest].[Person]  WITH CHECK ADD  CONSTRAINT [FK__Person__ParentPe__7F4CAA17] FOREIGN KEY([ParentPersonID])
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

/****** Object:  StoredProcedure [CEFTest].[up_Person_d]    Script Date: 12/6/2017 8:39:10 AM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE PROCEDURE CEFTest.[up_Person_d]
    @RetVal int = NULL OUTPUT,
    @Msg varchar(200) = NULL OUTPUT,
    @PersonID int
    , @LastUpdatedBy varchar(50) = NULL
    
AS
/***********************************************************
Name:  CEFTest.up_Person_d
Date: 12/5/2017 8:29 PM
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


DELETE [CEFTest].[Person]
WHERE
	PersonID = @PersonID

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

/****** Object:  StoredProcedure [CEFTest].[up_Person_u]    Script Date: 12/6/2017 8:39:10 AM ******/
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
	, @LastUpdatedBy varchar(50)
	, @LastUpdatedDate datetime2 = NULL OUTPUT
AS
/***********************************************************
Name:  CEFTest.up_Person_u    
Date: 12/5/2017 8:29 PM
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
	, LastUpdatedBy = @LastUpdatedBy
	, LastUpdatedDate = @NewLastUpdatedDate
WHERE
	CEFTest.Person.PersonID = @PersonID


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

/****** Object:  StoredProcedure [CEFTest].[up_Person_i]    Script Date: 12/6/2017 8:39:10 AM ******/
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
	, @LastUpdatedBy varchar(50)
	, @LastUpdatedDate datetime2 = NULL OUTPUT
AS
/***********************************************************
Name:  CEFTest.up_Person_i
Date: 12/5/2017 8:29 PM
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
	, LastUpdatedBy
	, LastUpdatedDate )
VALUES (
	@Name
	, @Age
	, @ParentPersonID
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

/****** Object:  StoredProcedure [CEFTest].[up_Person_ByKey]    Script Date: 12/6/2017 8:39:11 AM ******/
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
Date: 12/5/2017 8:29 PM
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

/****** Object:  StoredProcedure [CEFTest].[up_Phone_d]    Script Date: 12/6/2017 8:39:11 AM ******/
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
Date: 12/5/2017 8:29 PM
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

/****** Object:  StoredProcedure [CEFTest].[up_Phone_u]    Script Date: 12/6/2017 8:39:11 AM ******/
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
	, @LastUpdatedBy varchar(50)
	, @LastUpdatedDate datetime2 = NULL OUTPUT
AS
/***********************************************************
Name:  CEFTest.up_Phone_u    
Date: 12/5/2017 8:29 PM
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


UPDATE [CEFTest].[Phone]
SET
	PhoneTypeID = @PhoneTypeID
	, Number = @Number
	, LastUpdatedBy = @LastUpdatedBy
	, LastUpdatedDate = @NewLastUpdatedDate
WHERE
	CEFTest.Phone.PhoneID = @PhoneID


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

/****** Object:  StoredProcedure [CEFTest].[up_Phone_i]    Script Date: 12/6/2017 8:39:11 AM ******/
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
	, @LastUpdatedBy varchar(50)
	, @LastUpdatedDate datetime2 = NULL OUTPUT
AS
/***********************************************************
Name:  CEFTest.up_Phone_i
Date: 12/5/2017 8:29 PM
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
	, LastUpdatedBy
	, LastUpdatedDate )
VALUES (
	@PhoneTypeID
	, @Number
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

/****** Object:  StoredProcedure [CEFTest].[up_Phone_ByKey]    Script Date: 12/6/2017 8:39:11 AM ******/
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
Date: 12/5/2017 8:29 PM
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