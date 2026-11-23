// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

namespace MigraDoc.DocumentObjectModel
{
    /// <summary>
    /// A PageBreak is used to put following elements on a new page.
    /// </summary>
    public class PageBreak : DocumentObject
    {
        /// <summary>
        /// Initializes a new instance of the PageBreak class.
        /// </summary>
        public PageBreak()
        {
            BaseValues = new PageBreakValues(this);
        }

        /// <summary>
        /// Initializes a new instance of the PageBreak class with the specified parent.
        /// </summary>
        internal PageBreak(DocumentObject parent) : base(parent)
        {
            BaseValues = new PageBreakValues(this);
        }

        /// <summary>
        /// Creates a deep copy of this object.
        /// </summary>
        public new PageBreak Clone()
            => (PageBreak)DeepCopy();

        /// <summary>
        /// Advances section body flow to the next column, or to the first
        /// column of the next page when there is no remaining column.
        /// </summary>
        public bool IsColumnBreak
        {
            get => Values.IsColumnBreak ?? false;
            set => Values.IsColumnBreak = value;
        }

        /// <summary>
        /// Gets or sets whether this column break reuses an automatic area advance
        /// that moved the immediately preceding paragraph wholly into the current area.
        /// Defaults to false. Applies only to PDF section body column breaks; split
        /// paragraphs and explicit page breaks do not qualify.
        /// </summary>
        public bool CoalesceWithPrecedingParagraphOverflow
        {
            get => Values.CoalesceWithPrecedingParagraphOverflow ?? false;
            set => Values.CoalesceWithPrecedingParagraphOverflow = value;
        }

        /// <summary>
        /// Converts PageBreak into DDL.
        /// </summary>
        internal override void Serialize(Serializer serializer) 
        {
            serializer.WriteLine(IsColumnBreak ? "\\columnbreak" : "\\pagebreak");
            if (Values.CoalesceWithPrecedingParagraphOverflow is not null)
            {
                serializer.BeginAttributes();
                serializer.WriteSimpleAttribute("CoalesceWithPrecedingParagraphOverflow", CoalesceWithPrecedingParagraphOverflow);
                serializer.EndAttributes();
            }
        }

        /// <summary>
        /// Returns the metaobject of this instance.
        /// </summary>
        internal override Meta Meta => TheMeta;

        static readonly Meta TheMeta = new(typeof(PageBreak));

        /// <summary>
        /// Gets the underlying internal nullable values for the properties of this document object.
        /// </summary>
        public PageBreakValues Values => (PageBreakValues)BaseValues;

        /// <summary>
        /// Contains the internal nullable values for the properties of the enclosing document object class.
        /// </summary>
        public class PageBreakValues : Values
        {
            internal PageBreakValues(DocumentObject owner) : base(owner)
            { }

            public bool? IsColumnBreak { get; set; }

            public bool? CoalesceWithPrecedingParagraphOverflow { get; set; }
        }
    }
}
