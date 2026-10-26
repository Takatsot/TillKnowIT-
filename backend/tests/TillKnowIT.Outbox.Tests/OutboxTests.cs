using System.Text.Json;
using Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TillKnowIT.Outbox;
using TillKnowIT.SharedKernel;

namespace TillKnowIT.Outbox.Tests;

public class OutboxTests
{
    private sealed class RecordingHandler : IDomainEventHandler<ShopCreated>
    {
        public List<ShopCreated> Seen { get; } = [];
        public Task Handle(ShopCreated e, CancellationToken ct) { Seen.Add(e); return Task.CompletedTask; }
    }

    private sealed class ThrowingHandler : IDomainEventHandler<ShopCreated>
    {
        public Task Handle(ShopCreated e, CancellationToken ct) => throw new InvalidOperationException("boom");
    }

    private static OutboxMessage MessageFor(ShopCreated e) => new()
    {
        Type = e.GetType().AssemblyQualifiedName!,
        Payload = JsonSerializer.Serialize(e),
    };

    [Fact]
    public async Task Dispatches_the_event_with_all_its_data_to_every_handler()
    {
        var first = new RecordingHandler();
        var second = new RecordingHandler();
        var services = new ServiceCollection()
            .AddSingleton<IDomainEventHandler<ShopCreated>>(first)
            .AddSingleton<IDomainEventHandler<ShopCreated>>(second)
            .BuildServiceProvider();
        var original = new ShopCreated(Guid.NewGuid(), Guid.NewGuid(), "Mama's Spaza");

        await OutboxEventDispatcher.Dispatch(MessageFor(original), services, default);

        var received = Assert.Single(first.Seen);
        Assert.Equal(original, received);
        Assert.Equal(original.EventId, received.EventId);
        Assert.Single(second.Seen);
    }

    [Fact]
    public async Task Event_with_no_handlers_is_fine()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        await OutboxEventDispatcher.Dispatch(MessageFor(new ShopCreated(Guid.NewGuid(), Guid.NewGuid(), "x")), services, default);
    }

    [Fact]
    public async Task A_failing_handler_surfaces_the_error_so_the_row_can_be_retried()
    {
        var services = new ServiceCollection()
            .AddSingleton<IDomainEventHandler<ShopCreated>, ThrowingHandler>()
            .BuildServiceProvider();

        var ex = await Assert.ThrowsAsync<System.Reflection.TargetInvocationException>(() =>
            OutboxEventDispatcher.Dispatch(MessageFor(new ShopCreated(Guid.NewGuid(), Guid.NewGuid(), "x")), services, default));
        Assert.Equal("boom", ex.InnerException!.Message);
    }

    [Fact]
    public async Task Unknown_event_type_is_an_error()
    {
        var message = new OutboxMessage { Type = "Nope.DoesNotExist, Nope", Payload = "{}" };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            OutboxEventDispatcher.Dispatch(message, new ServiceCollection().BuildServiceProvider(), default));
    }

    private sealed class Thing : Entity
    {
        public Thing() => Raise(new ShopCreated(Id, Guid.NewGuid(), "Mama's Spaza"));
    }

    private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
    {
        public DbSet<Thing> Things => Set<Thing>();
        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<Thing>().HasKey(x => x.Id);
            b.AddOutbox();
        }
    }

    [Fact]
    public async Task Saving_an_entity_stores_its_events_in_the_outbox_in_the_same_save()
    {
        var options = new DbContextOptionsBuilder<TestContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new OutboxInterceptor())
            .Options;
        await using var db = new TestContext(options);

        var thing = new Thing();
        db.Things.Add(thing);
        await db.SaveChangesAsync();

        var row = await db.Set<OutboxMessage>().SingleAsync();
        Assert.Null(row.ProcessedAt);
        Assert.Equal(0, row.Attempts);
        var stored = (ShopCreated)JsonSerializer.Deserialize(row.Payload, Type.GetType(row.Type)!)!;
        Assert.Equal(thing.Id, stored.ShopId);
        Assert.Empty(thing.DomainEvents);
    }
}
