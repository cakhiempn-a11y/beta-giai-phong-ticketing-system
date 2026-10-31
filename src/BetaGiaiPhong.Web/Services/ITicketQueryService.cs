using BetaGiaiPhong.Web.Models.Responses;
using BetaGiaiPhong.Web.Models.ViewModels;

namespace BetaGiaiPhong.Web.Services;

public interface ITicketQueryService
{
    Task<OrderTicketsViewModel?> GetCustomerOrderTicketsAsync(
        long customerId, long orderId, CancellationToken ct = default);

    Task<IReadOnlyList<CustomerTicketDto>> GetCustomerTicketDtosAsync(
        long customerId, long orderId, CancellationToken ct = default);

    Task<byte[]?> GenerateQrCodePngBytesAsync(
        long ticketId, long customerId, CancellationToken ct = default);
}
