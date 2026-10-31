-- BGP - SQL Server physical schema v1.0
-- Execute against a NEW, EMPTY database, already selected in SSMS / Azure Data Studio.
-- Proposed one-cinema prototype: not an export of Beta Cinemas production schema.
-- No DROP commands, no real payment data. Compatibility SQL Server 2019+ recommended.
-- IMPORTANT: run business mutations through a transactional service (see Design Notes).
SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO
SET ANSI_PADDING ON;
GO

CREATE TABLE dbo.[Users] (
    [user_id] BIGINT IDENTITY(1,1) NOT NULL,
    [email] NVARCHAR(256) NOT NULL,
    [normalized_email] NVARCHAR(256) NOT NULL,
    [password_hash] NVARCHAR(512) NOT NULL,
    [display_name] NVARCHAR(120) NOT NULL,
    [status] VARCHAR(12) NOT NULL CONSTRAINT DF_Users_status DEFAULT ('ACTIVE'),
    [created_utc] DATETIME2(3) NOT NULL CONSTRAINT DF_Users_created_utc DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_Users PRIMARY KEY ([user_id]),
    CONSTRAINT UQ_Users_01 UNIQUE ([normalized_email]),
    CONSTRAINT CK_Users_01 CHECK (status IN ('ACTIVE','DISABLED'))
);
GO

CREATE TABLE dbo.[Roles] (
    [role_id] INT IDENTITY(1,1) NOT NULL,
    [role_code] VARCHAR(20) NOT NULL,
    [role_name] NVARCHAR(80) NOT NULL,
    CONSTRAINT PK_Roles PRIMARY KEY ([role_id]),
    CONSTRAINT UQ_Roles_01 UNIQUE ([role_code])
);
GO

CREATE TABLE dbo.[UserRoles] (
    [user_id] BIGINT NOT NULL,
    [role_id] INT NOT NULL,
    CONSTRAINT PK_UserRoles PRIMARY KEY ([user_id], [role_id]),
    CONSTRAINT FK_UserRoles_Users_01 FOREIGN KEY ([user_id]) REFERENCES dbo.[Users] ([user_id]) ON DELETE NO ACTION,
    CONSTRAINT FK_UserRoles_Roles_02 FOREIGN KEY ([role_id]) REFERENCES dbo.[Roles] ([role_id]) ON DELETE NO ACTION
);
GO

CREATE TABLE dbo.[Movies] (
    [movie_id] BIGINT IDENTITY(1,1) NOT NULL,
    [title] NVARCHAR(250) NOT NULL,
    [duration_min] SMALLINT NOT NULL,
    [genre_text] NVARCHAR(160) NULL,
    [age_label] NVARCHAR(24) NULL,
    [synopsis] NVARCHAR(MAX) NULL,
    [poster_url] NVARCHAR(500) NULL,
    [status] VARCHAR(12) NOT NULL CONSTRAINT DF_Movies_status DEFAULT ('DRAFT'),
    CONSTRAINT PK_Movies PRIMARY KEY ([movie_id]),
    CONSTRAINT CK_Movies_01 CHECK (duration_min BETWEEN 1 AND 480),
    CONSTRAINT CK_Movies_02 CHECK (status IN ('DRAFT','PUBLISHED','ARCHIVED'))
);
GO

CREATE TABLE dbo.[Rooms] (
    [room_id] BIGINT IDENTITY(1,1) NOT NULL,
    [room_code] VARCHAR(24) NOT NULL,
    [room_name] NVARCHAR(100) NOT NULL,
    [status] VARCHAR(12) NOT NULL CONSTRAINT DF_Rooms_status DEFAULT ('ACTIVE'),
    CONSTRAINT PK_Rooms PRIMARY KEY ([room_id]),
    CONSTRAINT UQ_Rooms_01 UNIQUE ([room_code]),
    CONSTRAINT CK_Rooms_01 CHECK (status IN ('ACTIVE','INACTIVE'))
);
GO

CREATE TABLE dbo.[SeatTypes] (
    [seat_type_code] VARCHAR(20) NOT NULL,
    [type_name] NVARCHAR(80) NOT NULL,
    [is_active] BIT NOT NULL CONSTRAINT DF_SeatTypes_is_active DEFAULT (1),
    CONSTRAINT PK_SeatTypes PRIMARY KEY ([seat_type_code])
);
GO

CREATE TABLE dbo.[Seats] (
    [seat_id] BIGINT IDENTITY(1,1) NOT NULL,
    [room_id] BIGINT NOT NULL,
    [seat_code] VARCHAR(16) NOT NULL,
    [seat_type_code] VARCHAR(20) NOT NULL,
    [row_label] VARCHAR(8) NOT NULL,
    [column_no] SMALLINT NOT NULL,
    [is_active] BIT NOT NULL CONSTRAINT DF_Seats_is_active DEFAULT (1),
    CONSTRAINT PK_Seats PRIMARY KEY ([seat_id]),
    CONSTRAINT UQ_Seats_01 UNIQUE ([room_id], [seat_code]),
    CONSTRAINT UQ_Seats_02 UNIQUE ([seat_id], [room_id]),
    CONSTRAINT CK_Seats_01 CHECK (column_no > 0),
    CONSTRAINT FK_Seats_Rooms_01 FOREIGN KEY ([room_id]) REFERENCES dbo.[Rooms] ([room_id]) ON DELETE NO ACTION,
    CONSTRAINT FK_Seats_SeatTypes_02 FOREIGN KEY ([seat_type_code]) REFERENCES dbo.[SeatTypes] ([seat_type_code]) ON DELETE NO ACTION
);
GO

CREATE TABLE dbo.[Showtimes] (
    [showtime_id] BIGINT IDENTITY(1,1) NOT NULL,
    [movie_id] BIGINT NOT NULL,
    [room_id] BIGINT NOT NULL,
    [start_utc] DATETIME2(3) NOT NULL,
    [end_utc] DATETIME2(3) NOT NULL,
    [buffer_minutes] SMALLINT NOT NULL CONSTRAINT DF_Showtimes_buffer_minutes DEFAULT (15),
    [status] VARCHAR(12) NOT NULL CONSTRAINT DF_Showtimes_status DEFAULT ('DRAFT'),
    [sellable_capacity_snapshot] INT NULL,
    [created_by] BIGINT NULL,
    [created_utc] DATETIME2(3) NOT NULL CONSTRAINT DF_Showtimes_created_utc DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_Showtimes PRIMARY KEY ([showtime_id]),
    CONSTRAINT UQ_Showtimes_01 UNIQUE ([showtime_id], [room_id]),
    CONSTRAINT CK_Showtimes_01 CHECK (start_utc < end_utc),
    CONSTRAINT CK_Showtimes_02 CHECK (buffer_minutes >= 0),
    CONSTRAINT CK_Showtimes_03 CHECK (sellable_capacity_snapshot IS NULL OR sellable_capacity_snapshot >= 0),
    CONSTRAINT CK_Showtimes_04 CHECK (status IN ('DRAFT','ON_SALE','CLOSED','CANCELLED')),
    CONSTRAINT CK_Showtimes_05 CHECK (status <> 'ON_SALE' OR sellable_capacity_snapshot IS NOT NULL),
    CONSTRAINT FK_Showtimes_Movies_01 FOREIGN KEY ([movie_id]) REFERENCES dbo.[Movies] ([movie_id]) ON DELETE NO ACTION,
    CONSTRAINT FK_Showtimes_Rooms_02 FOREIGN KEY ([room_id]) REFERENCES dbo.[Rooms] ([room_id]) ON DELETE NO ACTION,
    CONSTRAINT FK_Showtimes_Users_03 FOREIGN KEY ([created_by]) REFERENCES dbo.[Users] ([user_id]) ON DELETE NO ACTION
);
GO

CREATE TABLE dbo.[SeatPrices] (
    [showtime_id] BIGINT NOT NULL,
    [seat_type_code] VARCHAR(20) NOT NULL,
    [amount_vnd] BIGINT NOT NULL,
    [updated_utc] DATETIME2(3) NOT NULL CONSTRAINT DF_SeatPrices_updated_utc DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_SeatPrices PRIMARY KEY ([showtime_id], [seat_type_code]),
    CONSTRAINT CK_SeatPrices_01 CHECK (amount_vnd > 0),
    CONSTRAINT FK_SeatPrices_Showtimes_01 FOREIGN KEY ([showtime_id]) REFERENCES dbo.[Showtimes] ([showtime_id]) ON DELETE NO ACTION,
    CONSTRAINT FK_SeatPrices_SeatTypes_02 FOREIGN KEY ([seat_type_code]) REFERENCES dbo.[SeatTypes] ([seat_type_code]) ON DELETE NO ACTION
);
GO

CREATE TABLE dbo.[Orders] (
    [order_id] BIGINT IDENTITY(1,1) NOT NULL,
    [order_code] VARCHAR(40) NOT NULL,
    [user_id] BIGINT NOT NULL,
    [showtime_id] BIGINT NOT NULL,
    [status] VARCHAR(20) NOT NULL CONSTRAINT DF_Orders_status DEFAULT ('PENDING_PAYMENT'),
    [seat_count] TINYINT NOT NULL,
    [subtotal_vnd] BIGINT NOT NULL,
    [fee_vnd] BIGINT NOT NULL CONSTRAINT DF_Orders_fee_vnd DEFAULT (0),
    [discount_vnd] BIGINT NOT NULL CONSTRAINT DF_Orders_discount_vnd DEFAULT (0),
    [total_vnd] BIGINT NOT NULL,
    [created_utc] DATETIME2(3) NOT NULL CONSTRAINT DF_Orders_created_utc DEFAULT (SYSUTCDATETIME()),
    [expires_utc] DATETIME2(3) NOT NULL,
    [paid_utc] DATETIME2(3) NULL,
    [row_ver] ROWVERSION,
    CONSTRAINT PK_Orders PRIMARY KEY ([order_id]),
    CONSTRAINT UQ_Orders_01 UNIQUE ([order_code]),
    CONSTRAINT UQ_Orders_02 UNIQUE ([order_id], [showtime_id]),
    CONSTRAINT CK_Orders_01 CHECK (seat_count BETWEEN 1 AND 8),
    CONSTRAINT CK_Orders_02 CHECK (subtotal_vnd >= 0),
    CONSTRAINT CK_Orders_03 CHECK (fee_vnd >= 0),
    CONSTRAINT CK_Orders_04 CHECK (discount_vnd >= 0),
    CONSTRAINT CK_Orders_05 CHECK (total_vnd >= 0),
    CONSTRAINT CK_Orders_06 CHECK (total_vnd = subtotal_vnd + fee_vnd - discount_vnd),
    CONSTRAINT CK_Orders_07 CHECK (expires_utc > created_utc),
    CONSTRAINT CK_Orders_08 CHECK (status IN ('PENDING_PAYMENT','PAID','FAILED','CANCELLED','EXPIRED','NEEDS_REVIEW')),
    CONSTRAINT CK_Orders_09 CHECK (((status = 'PAID' AND paid_utc IS NOT NULL) OR (status <> 'PAID' AND paid_utc IS NULL))),
    CONSTRAINT FK_Orders_Users_01 FOREIGN KEY ([user_id]) REFERENCES dbo.[Users] ([user_id]) ON DELETE NO ACTION,
    CONSTRAINT FK_Orders_Showtimes_02 FOREIGN KEY ([showtime_id]) REFERENCES dbo.[Showtimes] ([showtime_id]) ON DELETE NO ACTION
);
GO

CREATE TABLE dbo.[ShowtimeSeats] (
    [showtime_id] BIGINT NOT NULL,
    [seat_id] BIGINT NOT NULL,
    [room_id] BIGINT NOT NULL,
    [state] VARCHAR(12) NOT NULL CONSTRAINT DF_ShowtimeSeats_state DEFAULT ('AVAILABLE'),
    [owner_order_id] BIGINT NULL,
    [hold_expires_utc] DATETIME2(3) NULL,
    [row_ver] ROWVERSION,
    CONSTRAINT PK_ShowtimeSeats PRIMARY KEY ([showtime_id], [seat_id]),
    CONSTRAINT CK_ShowtimeSeats_01 CHECK (((state IN ('AVAILABLE','BLOCKED') AND owner_order_id IS NULL AND hold_expires_utc IS NULL) OR (state='HELD' AND owner_order_id IS NOT NULL AND hold_expires_utc IS NOT NULL) OR (state='SOLD' AND owner_order_id IS NOT NULL AND hold_expires_utc IS NULL))),
    CONSTRAINT FK_ShowtimeSeats_Showtimes_01 FOREIGN KEY ([showtime_id], [room_id]) REFERENCES dbo.[Showtimes] ([showtime_id], [room_id]) ON DELETE NO ACTION,
    CONSTRAINT FK_ShowtimeSeats_Seats_02 FOREIGN KEY ([seat_id], [room_id]) REFERENCES dbo.[Seats] ([seat_id], [room_id]) ON DELETE NO ACTION,
    CONSTRAINT FK_ShowtimeSeats_Orders_03 FOREIGN KEY ([owner_order_id], [showtime_id]) REFERENCES dbo.[Orders] ([order_id], [showtime_id]) ON DELETE NO ACTION
);
GO

CREATE TABLE dbo.[OrderSeats] (
    [order_id] BIGINT NOT NULL,
    [showtime_id] BIGINT NOT NULL,
    [seat_id] BIGINT NOT NULL,
    [seat_type_snapshot] VARCHAR(20) NOT NULL,
    [unit_price_vnd] BIGINT NOT NULL,
    CONSTRAINT PK_OrderSeats PRIMARY KEY ([order_id], [seat_id]),
    CONSTRAINT UQ_OrderSeats_01 UNIQUE ([order_id], [showtime_id], [seat_id]),
    CONSTRAINT CK_OrderSeats_01 CHECK (unit_price_vnd > 0),
    CONSTRAINT FK_OrderSeats_Orders_01 FOREIGN KEY ([order_id], [showtime_id]) REFERENCES dbo.[Orders] ([order_id], [showtime_id]) ON DELETE NO ACTION,
    CONSTRAINT FK_OrderSeats_ShowtimeSeats_02 FOREIGN KEY ([showtime_id], [seat_id]) REFERENCES dbo.[ShowtimeSeats] ([showtime_id], [seat_id]) ON DELETE NO ACTION
);
GO

CREATE TABLE dbo.[Payments] (
    [payment_id] BIGINT IDENTITY(1,1) NOT NULL,
    [order_id] BIGINT NOT NULL,
    [provider] VARCHAR(40) NOT NULL,
    [attempt_key] VARCHAR(80) NOT NULL,
    [provider_transaction_ref] VARCHAR(120) NULL,
    [amount_vnd] BIGINT NOT NULL,
    [status] VARCHAR(16) NOT NULL CONSTRAINT DF_Payments_status DEFAULT ('CREATED'),
    [created_utc] DATETIME2(3) NOT NULL CONSTRAINT DF_Payments_created_utc DEFAULT (SYSUTCDATETIME()),
    [resolved_utc] DATETIME2(3) NULL,
    CONSTRAINT PK_Payments PRIMARY KEY ([payment_id]),
    CONSTRAINT UQ_Payments_01 UNIQUE ([order_id], [attempt_key]),
    CONSTRAINT CK_Payments_01 CHECK (amount_vnd > 0),
    CONSTRAINT CK_Payments_02 CHECK (status IN ('CREATED','PENDING','SUCCESS','FAILED','NEEDS_REVIEW')),
    CONSTRAINT FK_Payments_Orders_01 FOREIGN KEY ([order_id]) REFERENCES dbo.[Orders] ([order_id]) ON DELETE NO ACTION
);
GO

CREATE TABLE dbo.[PaymentEvents] (
    [payment_event_id] BIGINT IDENTITY(1,1) NOT NULL,
    [payment_id] BIGINT NULL,
    [provider] VARCHAR(40) NOT NULL,
    [provider_event_ref] VARCHAR(120) NULL,
    [provider_transaction_ref] VARCHAR(120) NULL,
    [auth_result] VARCHAR(14) NOT NULL,
    [decision] VARCHAR(24) NOT NULL,
    [payload_sha256] BINARY(32) NULL,
    [received_utc] DATETIME2(3) NOT NULL CONSTRAINT DF_PaymentEvents_received_utc DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_PaymentEvents PRIMARY KEY ([payment_event_id]),
    CONSTRAINT CK_PaymentEvents_01 CHECK (auth_result IN ('VERIFIED','REJECTED')),
    CONSTRAINT CK_PaymentEvents_02 CHECK (decision IN ('APPLIED','DUPLICATE','REJECTED','NEEDS_REVIEW','CONFLICT','FAILED_RECORDED')),
    CONSTRAINT FK_PaymentEvents_Payments_01 FOREIGN KEY ([payment_id]) REFERENCES dbo.[Payments] ([payment_id]) ON DELETE NO ACTION
);
GO

CREATE TABLE dbo.[Tickets] (
    [ticket_id] BIGINT IDENTITY(1,1) NOT NULL,
    [ticket_code] VARCHAR(50) NOT NULL,
    [order_id] BIGINT NOT NULL,
    [showtime_id] BIGINT NOT NULL,
    [seat_id] BIGINT NOT NULL,
    [status] VARCHAR(12) NOT NULL CONSTRAINT DF_Tickets_status DEFAULT ('ISSUED'),
    [qr_token_hash] BINARY(32) NOT NULL,
    [qr_token_cipher] VARBINARY(MAX) NOT NULL,
    [qr_key_id] VARCHAR(32) NOT NULL,
    [issued_utc] DATETIME2(3) NOT NULL CONSTRAINT DF_Tickets_issued_utc DEFAULT (SYSUTCDATETIME()),
    [used_utc] DATETIME2(3) NULL,
    CONSTRAINT PK_Tickets PRIMARY KEY ([ticket_id]),
    CONSTRAINT UQ_Tickets_01 UNIQUE ([ticket_code]),
    CONSTRAINT UQ_Tickets_02 UNIQUE ([order_id], [seat_id]),
    CONSTRAINT UQ_Tickets_03 UNIQUE ([showtime_id], [seat_id]),
    CONSTRAINT UQ_Tickets_04 UNIQUE ([qr_token_hash]),
    CONSTRAINT CK_Tickets_01 CHECK (status IN ('ISSUED','USED')),
    CONSTRAINT CK_Tickets_02 CHECK (((status='ISSUED' AND used_utc IS NULL) OR (status='USED' AND used_utc IS NOT NULL))),
    CONSTRAINT FK_Tickets_OrderSeats_01 FOREIGN KEY ([order_id], [showtime_id], [seat_id]) REFERENCES dbo.[OrderSeats] ([order_id], [showtime_id], [seat_id]) ON DELETE NO ACTION
);
GO

CREATE TABLE dbo.[TicketScans] (
    [scan_id] BIGINT IDENTITY(1,1) NOT NULL,
    [ticket_id] BIGINT NOT NULL,
    [staff_user_id] BIGINT NOT NULL,
    [scanned_utc] DATETIME2(3) NOT NULL CONSTRAINT DF_TicketScans_scanned_utc DEFAULT (SYSUTCDATETIME()),
    [outcome] VARCHAR(24) NOT NULL,
    CONSTRAINT PK_TicketScans PRIMARY KEY ([scan_id]),
    CONSTRAINT CK_TicketScans_01 CHECK (outcome IN ('ACCEPTED','ALREADY_USED','WRONG_SHOWTIME','OUTSIDE_WINDOW','REJECTED')),
    CONSTRAINT FK_TicketScans_Tickets_01 FOREIGN KEY ([ticket_id]) REFERENCES dbo.[Tickets] ([ticket_id]) ON DELETE NO ACTION,
    CONSTRAINT FK_TicketScans_Users_02 FOREIGN KEY ([staff_user_id]) REFERENCES dbo.[Users] ([user_id]) ON DELETE NO ACTION
);
GO

CREATE TABLE dbo.[AuditLogs] (
    [audit_id] BIGINT IDENTITY(1,1) NOT NULL,
    [actor_user_id] BIGINT NULL,
    [event_type] VARCHAR(60) NOT NULL,
    [entity_type] VARCHAR(40) NULL,
    [entity_key] NVARCHAR(120) NULL,
    [correlation_id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_AuditLogs_correlation_id DEFAULT (NEWID()),
    [details_json] NVARCHAR(MAX) NULL,
    [created_utc] DATETIME2(3) NOT NULL CONSTRAINT DF_AuditLogs_created_utc DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_AuditLogs PRIMARY KEY ([audit_id]),
    CONSTRAINT CK_AuditLogs_01 CHECK (details_json IS NULL OR ISJSON(details_json) = 1),
    CONSTRAINT FK_AuditLogs_Users_01 FOREIGN KEY ([actor_user_id]) REFERENCES dbo.[Users] ([user_id]) ON DELETE NO ACTION
);
GO

CREATE INDEX IX_Showtimes_Room_Start ON dbo.Showtimes(room_id, start_utc) INCLUDE(end_utc, status, buffer_minutes);
GO
CREATE INDEX IX_Showtimes_Movie_Start ON dbo.Showtimes(movie_id, start_utc);
GO

CREATE INDEX IX_Orders_User_Created ON dbo.Orders(user_id, created_utc DESC);
GO
CREATE INDEX IX_Orders_Status_Expiry ON dbo.Orders(status, expires_utc);
GO
CREATE INDEX IX_Orders_Paid ON dbo.Orders(paid_utc) INCLUDE(total_vnd, status) WHERE status = 'PAID';
GO

CREATE INDEX IX_ShowtimeSeats_Owner_State ON dbo.ShowtimeSeats(owner_order_id, state) WHERE owner_order_id IS NOT NULL;
GO
CREATE INDEX IX_ShowtimeSeats_State_Expiry ON dbo.ShowtimeSeats(state, hold_expires_utc);
GO

CREATE UNIQUE INDEX UX_Payments_Provider_Ref ON dbo.Payments(provider, provider_transaction_ref) WHERE provider_transaction_ref IS NOT NULL;
GO
CREATE INDEX IX_Payments_Order_Created ON dbo.Payments(order_id, created_utc DESC);
GO

CREATE UNIQUE INDEX UX_PaymentEvents_Provider_EventRef ON dbo.PaymentEvents(provider, provider_event_ref) WHERE provider_event_ref IS NOT NULL;
GO
CREATE INDEX IX_PaymentEvents_Transaction ON dbo.PaymentEvents(provider, provider_transaction_ref, received_utc);
GO

CREATE INDEX IX_Tickets_Order ON dbo.Tickets(order_id);
GO

CREATE INDEX IX_TicketScans_Ticket_UTC ON dbo.TicketScans(ticket_id, scanned_utc DESC);
GO

CREATE INDEX IX_AuditLogs_Created ON dbo.AuditLogs(created_utc DESC);
GO
CREATE INDEX IX_AuditLogs_Actor_Created ON dbo.AuditLogs(actor_user_id, created_utc DESC);
GO

-- IMPORTANT: uniqueness at (showtime_id, seat_id) on ShowtimeSeats AND Tickets
-- stops duplicate showtime/seat stock and duplicate issued tickets, respectively.
-- Schedule overlaps, Order/Seat/Ticket state transitions and price/capacity snapshots
-- require serialized business transactions, not just declarative CHECK/UNIQUE constraints.
