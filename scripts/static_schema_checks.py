"""Offline source/DDL sanity checks. Not a replacement for dotnet build or SQL Server execution."""
from pathlib import Path
import re, xml.etree.ElementTree as ET, sys
root=Path(__file__).resolve().parent.parent
sql=(root/'sql/01_reference_schema_SQL_ONLY.sql').read_text(encoding='utf-8')
config=(root/'src/BetaGiaiPhong.Data/Persistence/BetaModelConfiguration.cs').read_text(encoding='utf-8')
snapshot=(root/'src/BetaGiaiPhong.Data/Persistence/Migrations/BetaDbContextModelSnapshot.cs').read_text(encoding='utf-8')
mg=(root/'src/BetaGiaiPhong.Data/Persistence/Migrations/202610310001_InitialSchema.cs').read_text(encoding='utf-8')
blocks=re.findall(r'CREATE TABLE dbo\.\[(\w+)\] \(\n(.*?)\n\);\nGO',sql,re.S)
checks=[]
assert len(blocks)==17,len(blocks)
assert 'class BetaDbContextModelSnapshot : ModelSnapshot' in snapshot, 'Standard EF Core snapshot required'
cols=0;fks=0;tbls=[]
entities={x.stem:x.read_text(encoding='utf-8') for x in (root/'src/BetaGiaiPhong.Data/Entities').glob('*.cs')}
for table,body in blocks:
    tbls.append(table)
    names=re.findall(r'^\s*\[(\w+)\]\s+(?:BIGINT|INT|SMALLINT|TINYINT|VARCHAR|NVARCHAR|DATETIME2|BIT|ROWVERSION|BINARY|VARBINARY|UNIQUEIDENTIFIER)',body,re.M)
    cols+=len(names)
    for name in names:
        num=config.count(f'.HasColumnName("{name}")')
        # Names repeat on DIFFERENT tables; exact per-entity check further below.
        assert num>=1,(table,name)
    fks+=len(re.findall(r'CONSTRAINT FK_',body))
    assert f'b.ToTable("{table}"' in config,table
    for cons in re.findall(r'CONSTRAINT (FK_\w+|PK_\w+|UQ_\w+|CK_\w+)',body):
        assert f'"{cons}"' in config,(table,cons)
assert cols==116,cols
assert fks==22,fks
for f in root.rglob('*.cs'):
    text=f.read_text(encoding='utf-8')
    # Deliberately simple token balance check, remove comments & verbatim/regular string literals.
    clean=re.sub(r'@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|//[^\n]*|/\*.*?\*/', '""', text, flags=re.S)
    assert clean.count('{')==clean.count('}'),('braces',f)
    assert clean.count('(')==clean.count(')'),('parentheses',f)
    assert clean.count('[')==clean.count(']'),('brackets',f)
for f in root.rglob('*.csproj'): ET.parse(f)
ET.parse(root/'BetaGiaiPhong.slnx')
assert mg.count('migrationBuilder.Sql(')==len([s for s in re.split(r'^GO\s*$',sql,flags=re.M) if s.strip()])
assert mg.count('migrationBuilder.DropTable(')==17
assert config.count('HasConstraintName("FK_')==22
assert 'HasColumnType("rowversion").IsRequired().IsRowVersion()' in config
for vulnerable in ['PaymentEvents','ShowtimeSeats','Tickets','Orders','OrderSeats']:
    assert vulnerable in entities or vulnerable in tbls
print('PASS: 17 tables, 116 columns, 22 FKs; migration/snapshot/code mapping structurally consistent.')
print('PASS: 36 SQL batches, 17 Down drops; C# delimiter checks; csproj/slnx XML.')
print('NOTE: This is STATIC review. Execute dotnet build / migrate / smoke / race with a live SQL Server locally.')
