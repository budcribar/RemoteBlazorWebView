[CollectionDefinition("Server caching collection", DisableParallelization = true)]
public class ServerCachingCollection : ICollectionFixture<ServerFixture>, ICollectionFixture<ClientFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}
