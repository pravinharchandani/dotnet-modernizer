using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace LegacyShop.Core
{
    /// <summary>
    /// Archives orders to a legacy binary format. Not yet migrated:
    /// still uses BinaryFormatter, which is removed in modern .NET.
    /// </summary>
    public class LegacyOrderArchiver
    {
        public void Archive(Order order, string path)
        {
            var formatter = new BinaryFormatter();
            using (FileStream stream = File.Create(path))
            {
                formatter.Serialize(stream, order);
            }
        }

        public Order Restore(string path)
        {
            var formatter = new BinaryFormatter();
            using (FileStream stream = File.OpenRead(path))
            {
                return (Order)formatter.Deserialize(stream);
            }
        }
    }
}
