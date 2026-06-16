import csv
import os
from lxml import etree

def update_spclans(csv_file_path, xml_file_path):
    # Check if files exist
    if not os.path.exists(csv_file_path):
        print(f"Error: {csv_file_path} not found.")
        return
    if not os.path.exists(xml_file_path):
        print(f"Error: {xml_file_path} not found.")
        return

    # Load the XML
    parser = etree.XMLParser(remove_blank_text=False)
    tree = etree.parse(xml_file_path, parser)
    root = tree.getroot()

    # Open and read the CSV
    with open(csv_file_path, mode='r', encoding='utf-8') as f:
        reader = csv.DictReader(f)
        
        # Track updates for a summary
        updates_count = 0

        for row in reader:
            clan_id = row['clan_id'].strip()
            new_name = row['clan_name'].strip()

            # Find the Faction element with the matching id
            # Note: Bannerlord XMLs usually have Faction as a child of the root
            faction = root.find(f".//Faction[@id='{clan_id}']")

            if faction is not None:
                # Replace the name attribute
                # We remove the {=XXXXXXX} localization tag to force the raw string
                faction.set('name', new_name)
                print(f"Updated {clan_id}: New Name -> {new_name}")
                updates_count += 1
            else:
                print(f"Warning: Clan ID '{clan_id}' not found in XML.")

    # Save the modified XML
    tree.write(xml_file_path, encoding='utf-8', xml_declaration=True, pretty_print=True)
    print(f"\nSuccess! Total clans updated: {updates_count}")

# Execution
if __name__ == "__main__":
    # Ensure your CSV has a column named 'clan_id' matching the XML 'id'
    update_spclans('Mod data/Clans_data.csv', 'tyt_spclans.xml')