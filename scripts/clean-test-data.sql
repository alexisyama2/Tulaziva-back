/*
  Removes the data created by scripts/smoke-test.sh from the development database: the users with an email
  @smoke.tulaziva.test and what hangs from them: their favorites, their blocks, the reports they made or that are about their ads,
  their chats (messages and attachments go with them) and, if any is left, their ads (their images go with them).
  Their email tokens go with the users. Real users are not touched.

  Run it with (use the server and database of ConnectionStrings:Tulaziva; -C trusts the development certificate):
    sqlcmd -S "localhost\SQLEXPRESS" -d Tulaziva -C -i scripts/clean-test-data.sql

  Files of ads or chats that were deleted here and not through the API stay in src/Tulaziva.Api/storage. The smoke test
  deletes its own ads through the API and removes the files of its chat, so normally there are none.
*/
SET NOCOUNT ON;

DELETE f
FROM dbo.Favorites AS f
JOIN dbo.Users AS u ON u.Id = f.UserId
WHERE u.Email LIKE N'%@smoke.tulaziva.test';

-- The reports do not have a foreign key to the ad, so the ones about the ads that are deleted below are removed by ad id.
DELETE b
FROM dbo.UserBlocks AS b
JOIN dbo.Users AS u ON u.Id = b.BlockerId OR u.Id = b.BlockedId
WHERE u.Email LIKE N'%@smoke.tulaziva.test';

DELETE r
FROM dbo.AdReports AS r
WHERE r.ReporterId IN (SELECT Id FROM dbo.Users WHERE Email LIKE N'%@smoke.tulaziva.test')
   OR r.AdId IN (SELECT a.Id FROM dbo.Ads AS a JOIN dbo.Users AS u ON u.Id = a.OwnerId WHERE u.Email LIKE N'%@smoke.tulaziva.test');

DELETE c
FROM dbo.Conversations AS c
JOIN dbo.Users AS u ON u.Id = c.InitiatorId OR u.Id = c.RecipientId
WHERE u.Email LIKE N'%@smoke.tulaziva.test';

DELETE a
FROM dbo.Ads AS a
JOIN dbo.Users AS u ON u.Id = a.OwnerId
WHERE u.Email LIKE N'%@smoke.tulaziva.test';

DELETE FROM dbo.Users
WHERE Email LIKE N'%@smoke.tulaziva.test';

SELECT
    (SELECT COUNT(*) FROM dbo.Users) AS Users,
    (SELECT COUNT(*) FROM dbo.Ads) AS Ads,
    (SELECT COUNT(*) FROM dbo.Favorites) AS Favorites,
    (SELECT COUNT(*) FROM dbo.AdReports) AS Reports,
    (SELECT COUNT(*) FROM dbo.Conversations) AS Conversations,
    (SELECT COUNT(*) FROM dbo.Messages) AS Messages,
    (SELECT COUNT(*) FROM dbo.Categories) AS Categories;
