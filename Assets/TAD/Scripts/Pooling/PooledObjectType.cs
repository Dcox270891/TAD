namespace TAD.Pooling
{
    public enum PooledObjectType
    {
        Attacker,
        Defender,
        Projectile
    }

    /// <summary>
    /// Lets pooled gameplay objects reset their short-lived state on reuse.
    /// </summary>
    public interface IPoolLifecycle
    {
        void OnTakenFromPool();
        void OnReturnedToPool();
    }
}
