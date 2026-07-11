using MachineDataAcquisitionSystem.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Yitter.IdGenerator;

namespace MachineDataAcquisitionSystem
{
    internal static class Program
    {
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main()
        {

            // 确保只初始化一次
            var options = new IdGeneratorOptions(1);
            YitIdHelper.SetIdGenerator(options);

            // 初始化数据库
            DatabaseHelper.Initialize();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }
    }
}
