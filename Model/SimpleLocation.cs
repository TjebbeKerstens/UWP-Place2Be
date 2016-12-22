using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Place2Be.Model
{
    class SimpleLocation
    {
        private int Id { get; set; }
        private string Name { get; set; }
        private string Address { get; set; }

        public SimpleLocation(int id, string name, string address)
        {
            Id = id;
            Name = name;
            Address = address;
        }
    }
}
