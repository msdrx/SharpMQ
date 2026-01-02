namespace Sample.App.Shared;

public class TestMessage
{
    public decimal Amount { get; set; }
    public string? Account { get; set; }

    public override string ToString() => $"Account={Account}, Amount={Amount:C}";
}
