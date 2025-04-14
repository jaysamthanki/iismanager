using System;
using System.Collections.Generic;

namespace Techie.IISManager.Structures.Dtos
{
    /// <summary>
    /// 
    /// </summary>
    public class SecureBindingRequestDto
    {
        /// <summary>
        /// 
        /// </summary>
        public long WebSiteId { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public List<Guid> WebSiteBindingIds { get; set; } = [];
    }
}
