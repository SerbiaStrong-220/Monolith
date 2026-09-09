using Robust.Shared.Serialization;

namespace Content.Shared._NF.Bank.BUI;

[NetSerializable, Serializable]
public sealed class BankATMMenuInterfaceState : BoundUserInterfaceState
{
    /// <summary>
    /// bank balance of the character using the atm
    /// </summary>
    public int Balance;

    /// <summary>
    /// Savings of the player using the ATM.
    /// </summary>
    public long Savings;

    /// <summary>
    /// are the buttons enabled
    /// </summary>
    public bool Enabled;

    /// <summary>
    /// how much cash is inserted
    /// </summary>
    public int Deposit;

    // Exodus-begin corporate ATM commission display
    /// <summary>
    /// Amount credited to the character's sector account after all fees and savings deductions.
    /// </summary>
    public int SectorDeposit;

    /// <summary>
    /// Amount redirected to the character's savings account.
    /// </summary>
    public int SavingsDeposit;

    /// <summary>
    /// Fee charged by this ATM.
    /// </summary>
    public int AtmFee;

    /// <summary>
    /// Fee charged according to the character's company.
    /// </summary>
    public int CompanyCommission;
    // Exodus-end

    public BankATMMenuInterfaceState(int balance, long savings, bool enabled, int deposit)
    {
        Balance = balance;
        Savings = savings;
        Enabled = enabled;
        Deposit = deposit;
    }
}
