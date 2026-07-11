using System;
using SqlSugar;

namespace MachineDataAcquisitionSystem.Models
{
    /// <summary>
    /// 
    /// </summary>
    [SugarTable("TBL_EAP_DATE")]
    public class TBL_EAP_DATE
    {

        /// <summary>
        /// 主键ID（雪花ID）
        /// </summary>
        public long CID { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CDATETIME_CREATED { get; set; }

        /// <summary>
        /// 修改时间
        /// </summary>
        public DateTime CDATETIME_MODIFIED { get; set; }

        /// <summary>
        /// 创建人
        /// </summary>
        public string CUSER_CREATED { get; set; }

        /// <summary>
        /// 修改人
        /// </summary>
        public string CUSER_MODIFIED { get; set; }

        /// <summary>
        /// 状态（A=有效）
        /// </summary>
        public string CSTATE { get; set; }

        /// <summary>
        /// 实例ID
        /// </summary>
        public string CINSTANCE_ID { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        public string CROWREMARK { get; set; }

        /// <summary>
        /// 企业编码
        /// </summary>
        public long CENTERPRISE_CODE { get; set; }

        /// <summary>
        /// 组织编码
        /// </summary>
        public long CORG_CODE { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public string ProductSn { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public decimal TestValue { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public DateTime TestTime { get; set; }
    }
}
