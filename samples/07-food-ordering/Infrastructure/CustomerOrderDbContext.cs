using Microsoft.EntityFrameworkCore;

namespace food_ordering.Infrastructure;

public sealed class CustomerOrderDbContext(DbContextOptions<CustomerOrderDbContext> options) : DbContext(options)
{
    public DbSet<CustomerOrder> Orders => Set<CustomerOrder>();
    public DbSet<CustomerPayment> Payments => Set<CustomerPayment>();
    public DbSet<CustomerDelivery> Deliveries => Set<CustomerDelivery>();
    public DbSet<OrderLifecycleEvent> LifecycleEvents => Set<OrderLifecycleEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CustomerOrder>(entity =>
        {
            entity.ToTable("Orders");
            entity.HasKey(order => order.Id);
            entity.Property(order => order.CustomerKey).IsRequired();
            entity.Property(order => order.Summary).IsRequired();
            entity.Property(order => order.PaymentMethod).IsRequired();
            entity.Property(order => order.Status).HasConversion<string>().IsRequired();
            entity.HasIndex(order => new { order.CustomerKey, order.CreatedAtUtc });
            entity.HasIndex(order => new { order.Status, order.NextTransitionAtUtc });
            entity.HasOne(order => order.Payment).WithOne(payment => payment.Order)
                .HasForeignKey<CustomerPayment>(payment => payment.OrderId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(order => order.Delivery).WithOne(delivery => delivery.Order)
                .HasForeignKey<CustomerDelivery>(delivery => delivery.OrderId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(order => order.Events).WithOne(entry => entry.Order)
                .HasForeignKey(entry => entry.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CustomerPayment>(entity =>
        {
            entity.ToTable("Payments");
            entity.HasKey(payment => payment.Id);
            entity.Property(payment => payment.Method).IsRequired();
            entity.Property(payment => payment.Status).HasConversion<string>().IsRequired();
        });

        modelBuilder.Entity<CustomerDelivery>(entity =>
        {
            entity.ToTable("Deliveries");
            entity.HasKey(delivery => delivery.Id);
            entity.Property(delivery => delivery.Status).HasConversion<string>().IsRequired();
        });

        modelBuilder.Entity<OrderLifecycleEvent>(entity =>
        {
            entity.ToTable("OrderLifecycleEvents");
            entity.HasKey(entry => entry.Id);
            entity.Property(entry => entry.Entity).IsRequired();
            entity.Property(entry => entry.Status).IsRequired();
            entity.HasIndex(entry => new { entry.OrderId, entry.OccurredAtUtc });
        });
    }
}
