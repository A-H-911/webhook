IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'WebhookDb')
    CREATE DATABASE WebhookDb;
GO
