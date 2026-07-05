using MondShield.Domain.Accounts;

namespace MondShield.Application.Accounts;

/// <summary>
/// A <see cref="ShieldAccount"/> joined to its owner's identity (email + full name), so admin
/// views can show who a trader is rather than a bare account id. The identity/auth store and
/// the account store are separate tables joined on <c>ShieldAccount.UserId</c>.
/// </summary>
public sealed record AccountWithUser(ShieldAccount Account, string Email, string FullName);
