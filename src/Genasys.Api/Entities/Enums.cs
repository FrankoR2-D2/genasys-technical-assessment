namespace Genasys.Api.Entities;

public enum OrderStatus
{
    Pending,
    Confirmed,
    Cancelled,
    Shipped
}

public enum PaymentStatus
{
    Pending,
    Completed,
    Failed
}

public enum ReservationStatus
{
    Active,
    Released,
    Consumed
}

public enum PaymentMethod
{
    CreditCard,
    PayPal,
    Eft,
    MockGateway
}

public enum UserRole
{
    Admin,
    Customer
}
