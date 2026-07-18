using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace LegacyShop.Core
{
    /// <summary>
    /// Persists order snapshots to disk between batch runs.
    /// Uses BinaryFormatter, which is removed in modern .NET.
    /// </summary>
    public class OrderSnapshotSerializer
    {
        public void Save(Order order, string path)
        {
            var formatter = new BinaryFormatter();
            using (FileStream stream = File.Create(path))
            {
                formatter.Serialize(stream, order);
            }
        }

        public Order Load(string path)
        {
            var formatter = new BinaryFormatter();
            using (FileStream stream = File.OpenRead(path))
            {
                return (Order)formatter.Deserialize(stream);
            }
        }
    }
}
