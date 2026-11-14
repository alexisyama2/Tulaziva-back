/*
  Empties the data of the development database and leaves it as a fresh install:
    - Kept:    the administrator account(s) (Role = 'Admin'), the categories, and the migration history.
    - Removed: every other user, their ads and images, chats, messages and attachments, favorites, reports, blocks,
               the single-use tokens (refresh, verification, password reset, email change) and the email log.

  It is destructive and cannot be undone. Use it only on a development database.

  Run it with (the server and database of ConnectionStrings:Tulaziva; -C trusts the development certificate):
    sqlcmd -S "localhost\SQLEXPRESS" -d Tulaziva -C -i scripts/reset-data.sql

  Stop the API first. Everything is deleted in one transaction: if something fails, nothing changes.

  The files of the ads, chats and photos are not in the database: delete the contents of src/Tulaziva.Api/storage by hand
  (or the folder set in Storage:RootPath), or they stay as orphan files.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

-- Children first, parents last (the foreign keys are Restrict on the users).
DELETE FROM dbo.MessageAttachments;
DELETE FROM dbo.Messages;
DELETE FROM dbo.Conversations;

DELETE FROM dbo.Favorites;
DELETE FROM dbo.AdReports;
DELETE FROM dbo.UserBlocks;

DELETE FROM dbo.AdImages;
DELETE FROM dbo.Ads;

DELETE FROM dbo.UserTokens;
DELETE FROM dbo.EmailLogs;

DELETE FROM dbo.Users WHERE Role <> N'Admin';

COMMIT TRANSACTION;

SELECT
    (SELECT COUNT(*) FROM dbo.Users) AS Users,
    (SELECT COUNT(*) FROM dbo.Ads) AS Ads,
    (SELECT COUNT(*) FROM dbo.AdImages) AS AdImages,
    (SELECT COUNT(*) FROM dbo.Conversations) AS Conversations,
    (SELECT COUNT(*) FROM dbo.Messages) AS Messages,
    (SELECT COUNT(*) FROM dbo.MessageAttachments) AS Attachments,
    (SELECT COUNT(*) FROM dbo.Favorites) AS Favorites,
    (SELECT COUNT(*) FROM dbo.AdReports) AS Reports,
    (SELECT COUNT(*) FROM dbo.UserBlocks) AS Blocks,
    (SELECT COUNT(*) FROM dbo.UserTokens) AS Tokens,
    (SELECT COUNT(*) FROM dbo.EmailLogs) AS EmailLogs,
    (SELECT COUNT(*) FROM dbo.Categories) AS Categories;

SELECT Email, Role FROM dbo.Users;
