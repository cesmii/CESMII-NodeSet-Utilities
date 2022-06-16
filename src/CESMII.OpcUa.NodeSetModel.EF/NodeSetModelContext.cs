using Microsoft.EntityFrameworkCore;
using System;

namespace CESMII.OpcUa.NodeSetModel
{
    public class NodeSetModelContext : DbContext
    {
        public NodeSetModelContext(DbContextOptions<NodeSetModelContext> options) : base(options)
        {
            // Blank
        }

        protected NodeSetModelContext(DbContextOptions options)
        {
            // Blank
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder
                .UseLazyLoadingProxies()
            ;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            CreateModel(modelBuilder);

        }
        public static void CreateModel(ModelBuilder modelBuilder)
        {
            modelBuilder.Owned<NodeModel.LocalizedText>();
            modelBuilder.Owned<NodeModel.NodeAndReference>();
            modelBuilder.Owned<VariableModel.EngineeringUnitInfo>();
            modelBuilder.Owned<DataTypeModel.StructureField>();
            modelBuilder.Owned<DataTypeModel.UaEnumField>();
            modelBuilder.Owned<RequiredModelInfo>();

            modelBuilder.Entity<NodeSetModel>()
                .ToTable("NodeSets")
                .Ignore(nsm => nsm.AllNodesByNodeId)
                .Ignore(nsm => nsm.CustomState)
                .HasKey(nsm => new { nsm.ModelUri, nsm.PublicationDate })
                ;
            modelBuilder.Entity<NodeSetModel>()
                .OwnsMany(nsm => nsm.RequiredModels).WithOwner()
                ;
            modelBuilder.Entity<NodeModel>()
                .Ignore(nm => nm.CustomState)
                .Ignore(nsm => nsm.OtherReferencingNodes) // Populated from nsm.OtherChildren in the NodeSetModel factories
                .Property<DateTime?>("NodeSetPublicationDate") // EF tooling does not properly infer the type of this auto-generated property when using it in a foreign key: workaround declare explcitly
                ;
            modelBuilder.Entity<NodeModel>()
                .ToTable("Nodes")
                .HasKey(
                    nameof(NodeModel.NodeId), 
                    $"{nameof(NodeModel.NodeSet)}{nameof(NodeSetModel.ModelUri)}",// Foreign key with auto-generated PK of the NodeModel.NodeSet property
                    $"{nameof(NodeModel.NodeSet)}{nameof(NodeSetModel.PublicationDate)}")
                ;

            modelBuilder.Entity<NodeModel>()
                .OwnsMany<NodeModel.NodeAndReference>(nm => nm.OtherReferencedNodes).WithOwner()
                ;

            modelBuilder.Entity<ObjectTypeModel>()
                .ToTable("ObjectTypes")
                ;
            modelBuilder.Entity<DataTypeModel>()
                .ToTable("DataTypes")
                ;
            modelBuilder.Entity<VariableTypeModel>()
                .ToTable("VariableTypes")
                ;
            modelBuilder.Entity<DataVariableModel>()
                .ToTable("DataVariables")
                ;
            modelBuilder.Entity<PropertyModel>()
                .ToTable("Properties")
                ;
            modelBuilder.Entity<ObjectModel>()
                .ToTable("Objects")
                .HasOne<ObjectTypeModel>(o => o.TypeDefinition).WithMany()
                ;

            modelBuilder.Entity<InterfaceModel>()
                .ToTable("Interfaces")
                ;

            modelBuilder.Entity<VariableModel>()
                .ToTable("Variables")
                .OwnsOne(v => v.EngineeringUnit).Property(v => v.NamespaceUri).IsRequired()
                ;
            modelBuilder.Entity<BaseTypeModel>()
                .ToTable("BaseTypes")
                .Ignore(m => m.SubTypes)
                ;
            modelBuilder.Entity<MethodModel>()
                .ToTable("Methods")
                ;
            modelBuilder.Entity<ReferenceTypeModel>()
                .ToTable("ReferenceTypes")
                ;
        }

        public DbSet<NodeSetModel> NodeSets { get; set; }
        public DbSet<NodeModel> NodeModels { get; set; }
    }

}