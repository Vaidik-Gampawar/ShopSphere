#!/usr/bin/env python3
"""Generate uncommitted local settings. Pass SHOPSPHERE_SQL without a database name."""
import os
from pathlib import Path
import secrets
import shlex
root=Path(__file__).resolve().parents[1]
target=root/'.local/dev.env'
if target.exists():
    raise SystemExit('Keeping existing .local/dev.env. Edit it explicitly to change settings.')
base=os.environ.get('SHOPSPHERE_SQL')
if not base:
    raise SystemExit('Set SHOPSPHERE_SQL to your SQL Server connection string, excluding Database/Initial Catalog. See docs/LOCAL_SETUP.md.')
import re
if re.search(r'(?:^|;)\s*(?:database|initial\s+catalog)\s*=', base, re.IGNORECASE):
    raise SystemExit('Exclude Database/Initial Catalog from SHOPSPHERE_SQL; three database names are appended.')
settings={
 'ASPNETCORE_ENVIRONMENT':'Development',
 'Jwt__Key':secrets.token_urlsafe(48),
 'Services__Key':secrets.token_urlsafe(48),
 'Seed__Admin__Email':'admin@shopsphere.test',
 'Seed__Admin__Password':'Admin-Aa1!'+secrets.token_urlsafe(18),
 'Seed__Customer__Email':'customer@shopsphere.test',
 'Seed__Customer__Password':'Customer-Aa1!'+secrets.token_urlsafe(18),
 'ProductConnection':base.rstrip(';')+';Database=ShopSphereProducts',
 'OrderConnection':base.rstrip(';')+';Database=ShopSphereOrders',
 'IdentityConnection':base.rstrip(';')+';Database=ShopSphereIdentity',
}
target.parent.mkdir(exist_ok=True)
fd=os.open(target,os.O_WRONLY|os.O_CREAT|os.O_EXCL,0o600)
with os.fdopen(fd,'w') as stream:
    stream.write('# Local development only. Generated secrets. Do not commit.\n')
    for key,value in settings.items():stream.write('export '+key+'='+shlex.quote(value)+'\n')
print('Created .local/dev.env (permissions 0600). Source it in every terminal. Demo login values are in this file.')
