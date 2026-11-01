using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BetaGiaiPhong.Data.Persistence;
using BetaGiaiPhong.Data.Services;
using BetaGiaiPhong.Web.Models.Requests;
using BetaGiaiPhong.Web.Models.Responses;

namespace BetaGiaiPhong.Web.Api;

[ApiController]
[Route("api/staff/tickets")]
[Authorize(Policy = "RequireStaff")]
public sealed class StaffTicketsApiController(
    TicketScanningService scanningService,
    BetaDbContext db) : ControllerBase
{
    [HttpPost("scan")]
    [ValidateAntiForgeryToken]
    [ProducesResponseType(typeof(ScanTicketResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ScanTicketResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ScanTicketResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ScanTicketResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ScanTicket([FromBody] ScanTicketRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "INVALID_REQUEST",
                Detail = "Dữ liệu yêu cầu soát vé không hợp lệ."
            });
        }

        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(rawUserId, out var staffUserId) || staffUserId <= 0)
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "UNAUTHORIZED",
                Detail = "Không xác định được danh tính nhân viên soát vé."
            });
        }

        try
        {
            var scanResult = await scanningService.ScanAsync(
                staffUserId,
                request.QrToken.Trim(),
                request.ExpectedShowtimeId,
                ct);

            // Enrich ticket context if ticketId was identified in database
            string? ticketCode = null;
            string? movieTitle = null;
            string? roomName = null;
            string? seatLabel = null;
            DateTime? showtimeStartUtc = null;
            DateTime? usedUtc = null;

            if (scanResult.TicketId.HasValue)
            {
                var ticketInfo = await (
                    from t in db.Tickets.AsNoTracking()
                    where t.TicketId == scanResult.TicketId.Value
                    join s in db.Seats.AsNoTracking() on t.SeatId equals s.SeatId
                    join st in db.Showtimes.AsNoTracking() on t.ShowtimeId equals st.ShowtimeId
                    join m in db.Movies.AsNoTracking() on st.MovieId equals m.MovieId
                    join r in db.Rooms.AsNoTracking() on st.RoomId equals r.RoomId
                    select new
                    {
                        t.TicketCode,
                        t.UsedUtc,
                        MovieTitle = m.Title,
                        RoomName = r.RoomName,
                        SeatLabel = s.RowLabel + s.ColumnNo.ToString(),
                        st.StartUtc
                    }
                ).SingleOrDefaultAsync(ct);

                if (ticketInfo != null)
                {
                    ticketCode = ticketInfo.TicketCode;
                    movieTitle = ticketInfo.MovieTitle;
                    roomName = ticketInfo.RoomName;
                    seatLabel = ticketInfo.SeatLabel;
                    showtimeStartUtc = ticketInfo.StartUtc;
                    usedUtc = ticketInfo.UsedUtc;
                }
            }

            var scannedUtc = DateTime.UtcNow;

            return scanResult.Outcome switch
            {
                "ACCEPTED" => Ok(new ScanTicketResponse(
                    Outcome: "ACCEPTED",
                    IsSuccess: true,
                    TicketId: scanResult.TicketId,
                    TicketCode: ticketCode,
                    Message: "Soát vé thành công. Chúc quý khách xem phim vui vẻ!",
                    ScannedUtc: scannedUtc,
                    MovieTitle: movieTitle,
                    RoomName: roomName,
                    SeatLabel: seatLabel,
                    ShowtimeStartUtc: showtimeStartUtc,
                    UsedUtc: usedUtc
                )),

                "ALREADY_USED" => Conflict(new ScanTicketResponse(
                    Outcome: "ALREADY_USED",
                    IsSuccess: false,
                    TicketId: scanResult.TicketId,
                    TicketCode: ticketCode,
                    Message: "Cảnh báo: Vé đã được sử dụng trước đó. Không thể vào rạp lần thứ hai.",
                    ScannedUtc: scannedUtc,
                    MovieTitle: movieTitle,
                    RoomName: roomName,
                    SeatLabel: seatLabel,
                    ShowtimeStartUtc: showtimeStartUtc,
                    UsedUtc: usedUtc
                )),

                "WRONG_SHOWTIME" => UnprocessableEntity(new ScanTicketResponse(
                    Outcome: "WRONG_SHOWTIME",
                    IsSuccess: false,
                    TicketId: scanResult.TicketId,
                    TicketCode: ticketCode,
                    Message: "Vé thuộc suất chiếu khác. Vui lòng kiểm tra lại phòng chiếu và giờ chiếu.",
                    ScannedUtc: scannedUtc,
                    MovieTitle: movieTitle,
                    RoomName: roomName,
                    SeatLabel: seatLabel,
                    ShowtimeStartUtc: showtimeStartUtc,
                    UsedUtc: usedUtc
                )),

                "OUTSIDE_WINDOW" => UnprocessableEntity(new ScanTicketResponse(
                    Outcome: "OUTSIDE_WINDOW",
                    IsSuccess: false,
                    TicketId: scanResult.TicketId,
                    TicketCode: ticketCode,
                    Message: "Ngoài khung giờ soát vé (chỉ được soát vé từ 30 phút trước giờ chiếu đến 15 phút sau khi kết thúc).",
                    ScannedUtc: scannedUtc,
                    MovieTitle: movieTitle,
                    RoomName: roomName,
                    SeatLabel: seatLabel,
                    ShowtimeStartUtc: showtimeStartUtc,
                    UsedUtc: usedUtc
                )),

                "REJECTED" => UnprocessableEntity(new ScanTicketResponse(
                    Outcome: "REJECTED",
                    IsSuccess: false,
                    TicketId: scanResult.TicketId,
                    TicketCode: ticketCode,
                    Message: "Vé bị từ chối do đơn hàng chưa hoàn tất thanh toán hợp lệ.",
                    ScannedUtc: scannedUtc,
                    MovieTitle: movieTitle,
                    RoomName: roomName,
                    SeatLabel: seatLabel,
                    ShowtimeStartUtc: showtimeStartUtc,
                    UsedUtc: usedUtc
                )),

                "NOT_FOUND" => NotFound(new ScanTicketResponse(
                    Outcome: "NOT_FOUND",
                    IsSuccess: false,
                    TicketId: null,
                    TicketCode: null,
                    Message: "Mã QR không hợp lệ hoặc không tồn tại trong hệ thống.",
                    ScannedUtc: scannedUtc
                )),

                _ => BadRequest(new ScanTicketResponse(
                    Outcome: scanResult.Outcome,
                    IsSuccess: false,
                    TicketId: scanResult.TicketId,
                    TicketCode: ticketCode,
                    Message: "Yêu cầu soát vé bị từ chối.",
                    ScannedUtc: scannedUtc
                ))
            };
        }
        catch (BusinessRuleException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = ex.Code,
                Detail = ex.Message
            });
        }
    }
}
