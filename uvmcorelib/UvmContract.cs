using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UvmCoreLib
{
    /**
     * uvm合约类型，使用时需要继承它来使用 
     */
    public abstract class UvmContract<T> where T : class
    {
        public string id { get; }
        public string name { get; }
        public T storage { get; set; }
        public UvmContract(T storage)
        {
            this.id = "demo_id";
            this.name = "demo_name";
            this.storage = storage;
        }
        public abstract void init();
        public virtual void on_deposit(long num)
        {

        }
        public virtual void on_upgrade()
        {

        }
        public virtual void on_destroy()
        {

        }
    }
}
