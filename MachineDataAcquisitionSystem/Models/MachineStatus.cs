using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MachineDataAcquisitionSystem.Models
{
    public enum MachineStatus
    {
        Stopped,    // 已停止
        Running,    // 运行中
        Error       // 异常
    }
    //日志等级
    public enum LogLevel
    {
        Info,       // 普通信息 - 绿色
        Success,    // 成功 - 深绿色
        Warning,    // 警告 - 橙色
        Error       // 错误 - 红色
    }
}
