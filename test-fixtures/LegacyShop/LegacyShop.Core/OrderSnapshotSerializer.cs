using System.IO;
using System.Text.Json;

namespace LegacyShop.Core
{
    /// <summary>
    /// Persists order snapshots to disk between batch runs.
    /// Uses System.Text.Json (BinaryFormatter was removed in modern .NET for security).
    /// </summary>
    public class OrderSnapshotSerializer
    {
        public void Save(Order order, string path)
        {
            // Migrated from BinaryFormatter to System.Text.Json.
            // Semantic differences vs. BinaryFormatter:
            // - Only public properties are serialized; private/internal fields are not.
            //   (Order/Product expose everything via public properties, so no data is lost here.)
            // - Reference cycles and shared references are not preserved by default.
            // - The on-disk format changed from binary to JSON: snapshots written by the old
            //   BinaryFormatter code cannot be read back by Load. Re-generate or migrate
            //   existing snapshot files.
            using (FileStream stream = File.Create(path))
            {
                JsonSerializer.Serialize(stream, order);
            }
        }

        public Order Load(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            {
                // Unlike BinaryFormatter, this only materializes the declared Order shape;
                // polymorphic/arbitrary embedded types are not deserialized.
                return JsonSerializer.Deserialize<Order>(stream);
            }
        }
    }
}
