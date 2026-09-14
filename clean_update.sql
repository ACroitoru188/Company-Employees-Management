BEGIN TRANSACTION;
CREATE TABLE [LeaveRequestDocuments] (
    [Id] uniqueidentifier NOT NULL,
    [LeaveRequestId] uniqueidentifier NOT NULL,
    [OriginalFileName] nvarchar(255) NOT NULL,
    [HashedFileName] nvarchar(255) NOT NULL,
    [ContentType] nvarchar(100) NOT NULL,
    CONSTRAINT [PK_LeaveRequestDocuments] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_LeaveRequestDocuments_LeaveRequests_LeaveRequestId] FOREIGN KEY ([LeaveRequestId]) REFERENCES [LeaveRequests] ([Id]) ON DELETE CASCADE
);

CREATE INDEX [IX_LeaveRequestDocuments_LeaveRequestId] ON [LeaveRequestDocuments] ([LeaveRequestId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260909091427_AddLeaveRequestDocuments', N'9.0.2');

COMMIT;
GO


