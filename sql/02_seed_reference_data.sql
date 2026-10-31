-- Demo only - run after 01_create_schema.sql
-- No real customer identity, payment credential, cinema schedule or financial data.
SET NOCOUNT ON;
IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE role_code = 'CUSTOMER') INSERT INTO dbo.Roles(role_code,role_name) VALUES('CUSTOMER',N'Khách hàng');
IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE role_code = 'STAFF') INSERT INTO dbo.Roles(role_code,role_name) VALUES('STAFF',N'Nhân viên soát vé');
IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE role_code = 'MANAGER') INSERT INTO dbo.Roles(role_code,role_name) VALUES('MANAGER',N'Người quản lý');
IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE role_code = 'ADMIN') INSERT INTO dbo.Roles(role_code,role_name) VALUES('ADMIN',N'Quản trị viên');
IF NOT EXISTS (SELECT 1 FROM dbo.SeatTypes WHERE seat_type_code='STANDARD') INSERT INTO dbo.SeatTypes(seat_type_code,type_name) VALUES('STANDARD',N'Ghế tiêu chuẩn');
IF NOT EXISTS (SELECT 1 FROM dbo.SeatTypes WHERE seat_type_code='VIP') INSERT INTO dbo.SeatTypes(seat_type_code,type_name) VALUES('VIP',N'Ghế VIP');
-- Populate demonstration Rooms, Movies, Seats, Showtimes via the manager portal;
-- all showtime data should be synthetic and explicitly labeled as test fixtures.
