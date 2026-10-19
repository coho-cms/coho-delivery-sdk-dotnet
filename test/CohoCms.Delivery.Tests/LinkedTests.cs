namespace CohoCms.Delivery.Tests;

public sealed class LinkedTests
{
    [Fact]
    public void Resolved_keeps_the_entries_that_are_here_in_order_and_leaves_out_stubs()
    {
        var first = new HydratedEntry { Id = "e1", Type = "tag", Slug = "traffic" };
        var second = new HydratedEntry { Id = "e2", Type = "tag", Slug = "drainage" };
        Linked<HydratedEntry>[] links = [Linked.To(first), Linked.Stub<HydratedEntry>("e9", UnresolvedReason.Absent), Linked.To(second)];

        Assert.Equal([first, second], links.Resolved());
    }
}
