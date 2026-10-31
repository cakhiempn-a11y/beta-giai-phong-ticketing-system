-- Non-destructive validation queries after loading demo data.
-- Integrity dimensions requiring service-level atomic contracts.
SELECT 'orphan/other-room showtime seat' AS check_name, COUNT_BIG(*) AS violation_count
FROM dbo.ShowtimeSeats ss JOIN dbo.Seats s ON s.seat_id=ss.seat_id
JOIN dbo.Showtimes st ON st.showtime_id=ss.showtime_id
WHERE s.room_id<>st.room_id OR ss.room_id<>st.room_id;
SELECT 'sold seat without paid owner' AS check_name,COUNT_BIG(*) violation_count
FROM dbo.ShowtimeSeats ss LEFT JOIN dbo.Orders o ON ss.owner_order_id=o.order_id
WHERE ss.state='SOLD' AND (o.order_id IS NULL OR o.status<>'PAID');
SELECT 'paid order tickets mismatch' AS check_name, COUNT_BIG(*) violation_count
FROM dbo.Orders o
OUTER APPLY (SELECT COUNT_BIG(*) n FROM dbo.Tickets t WHERE t.order_id=o.order_id) x
WHERE o.status='PAID' AND x.n<>o.seat_count;
SELECT 'paid order snapshot sum mismatch' AS check_name,COUNT_BIG(*) violation_count
FROM dbo.Orders o
OUTER APPLY (SELECT COALESCE(SUM(os.unit_price_vnd),0) v FROM dbo.OrderSeats os WHERE os.order_id=o.order_id) x
WHERE x.v<>o.subtotal_vnd;
SELECT 'late success payment not marked review' AS check_name,COUNT_BIG(*) violation_count
FROM dbo.Payments p JOIN dbo.Orders o ON o.order_id=p.order_id
WHERE p.status='SUCCESS' AND o.status IN ('EXPIRED','CANCELLED');
-- Run controlled concurrency tests (two simultaneous buyers / duplicate callbacks / duplicate scans)
-- through the application. A query alone cannot prove race safety.
