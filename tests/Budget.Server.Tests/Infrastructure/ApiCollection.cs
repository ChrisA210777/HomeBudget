namespace Budget.Server.Tests.Infrastructure;

/// <summary>
/// Tests in this collection share one <see cref="BudgetApiFactory"/>, and so one SQL Server
/// container. Add <c>[Collection(ApiCollection.Name)]</c> to each class that derives from
/// <see cref="IntegrationTestBase"/>.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<BudgetApiFactory>
{
    public const string Name = "Api";
}
