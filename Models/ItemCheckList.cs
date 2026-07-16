using System;
using System.Collections.Generic;
using System.Text;

namespace Aplicacion_SCA.Models
{
    public class ItemCheckList
    {
        public string ValorIntroducido { get; set; }
        public string FaseCheck { get; set; } = string.Empty;
        public string Check { get; set; } = string.Empty;
        public string ModeloCheck { get; set; } = string.Empty;
        public string Estado { get; set; } = string.Empty;
        public string Observacion { get; set; } = string.Empty;
    }
}
