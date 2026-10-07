import io

# Read the file
with open('c:/Aplicações & Sistemas/Sistema de Estoque/ControleEstoqueLoja/wwwroot/css/corporate-theme.css', 'r', encoding='utf-8') as f:
    lines = f.readlines()

# Find the old duplicated block
start_idx = None
end_idx = None
for i, line in enumerate(lines):
    if '/* 30 DIAS: barras mais finas e colunas mais espaçadas */' in line:
        start_idx = i
    if 'ETAPA C - GRAFICO 7/30 DIAS' in line:
        end_idx = i
        break

print(f'Old block found: lines {start_idx+1} to {end_idx}')

# Remove the old block (keep the ETAPA C comment and everything after)
new_lines = lines[:start_idx] + lines[end_idx:]

# Write the cleaned file
with open('c:/Aplicações & Sistemas/Sistema de Estoque/ControleEstoqueLoja/wwwroot/css/corporate-theme.css', 'w', encoding='utf-8') as f:
    f.writelines(new_lines)

print(f'Cleanup complete. Removed {len(lines) - len(new_lines)} lines.')
print(f'New file has {len(new_lines)} lines.')