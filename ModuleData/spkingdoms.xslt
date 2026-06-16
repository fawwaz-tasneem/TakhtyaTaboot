<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
    <xsl:output omit-xml-declaration="yes"/>

    <xsl:template match="@*|node()">
        <xsl:copy>
            <xsl:apply-templates select="@*|node()"/>
        </xsl:copy>
    </xsl:template>

    <xsl:template match="*[@id='empire']/@name">
        <xsl:attribute name="name">Gurkani Alamgir</xsl:attribute>
    </xsl:template>
    <xsl:template match="*[@id='empire']/@ruler_title">
        <xsl:attribute name="ruler_title">Shahenshah Al Sultan al Azam</xsl:attribute>
    </xsl:template>
    <xsl:template match="*[@id='empire']/@short_name">
        <xsl:attribute name="short_name">Mughliya Sultanat</xsl:attribute>
    </xsl:template>
    <xsl:template match="*[@id='empire']/@text">
        <xsl:attribute name="text">{=aEfsyVH4}The Mughal Empire was once the envy of the world, a superpower that commanded the loyalty of kings from Kabul to the Kaveri. Now, the Lion of the Timurids is old and toothless. While the Emperor still sits upon the Peacock Throne in Delhi, holding the exclusive right to grant titles and legitimacy, his command rarely extends beyond the city walls. The treasury is drained by centuries of war, and the court is a viper's nest of scheming nobles who care more for their own fiefs than the safety of the realm. Despite its decline, the Imperial army remains a terrifying sight on the open field, fielding the heaviest armored cavalry and the largest siege guns in the subcontinent. They fight with the desperation of a dying giant, convinced that they are the only civilized order in a world gone mad. To serve the Emperor is to uphold the ancient law; to defy him is treason.</xsl:attribute>
    </xsl:template>
    <xsl:template match="*[@id='empire']/@title">
        <xsl:attribute name="title">The Mughal Empire</xsl:attribute>
    </xsl:template>
    <xsl:template match="*[@id='empire_w']/@name">
        <xsl:attribute name="name">Subah e Bangaal</xsl:attribute>
    </xsl:template>
    <xsl:template match="*[@id='empire_w']/@ruler_title">
        <xsl:attribute name="ruler_title">Nawaab Ala ud Daulah</xsl:attribute>
    </xsl:template>
    <xsl:template match="*[@id='empire_w']/@short_name">
        <xsl:attribute name="short_name">Bangaal</xsl:attribute>
    </xsl:template>
    <xsl:template match="*[@id='empire_w']/@text">
        <xsl:attribute name="text">{=mOZsbV8j}While the rest of the subcontinent burns in the fires of war, Bengal glitters with gold. Known as the Paradise of Nations, this province has effectively seceded from the Empire, turning its massive textile and trading revenues into a private kingdom. The Nawabs of Bengal are merchant-princes who understand that gold is sharper than steel. They maintain a delicate peace, paying lip service to Delhi while ruling as absolute monarchs in the fertile delta. Bengals power lies not in martial tradition, but in its inexhaustible resources. Their armies are vast, comprised of well-paid professional infantry, war elephants, and vast batteries of artillery funded by the banking houses of the Jagat Seths. They may lack the warrior spirit of the north, but they can afford to lose battles that would bankrupt any other kingdom, simply buying their enemies' loyalty when force fails.</xsl:attribute>
    </xsl:template>
    <xsl:template match="*[@id='empire_w']/@title">
        <xsl:attribute name="title">Suba Bangaal</xsl:attribute>
    </xsl:template>
    <xsl:template match="*[@id='empire_s']/@name">
        <xsl:attribute name="name">Riyasat e Hyderabad</xsl:attribute>
    </xsl:template>
    <xsl:template match="*[@id='empire_s']/@ruler_title">
        <xsl:attribute name="ruler_title">Nizaam ul Mulk</xsl:attribute>
    </xsl:template>
    <xsl:template match="*[@id='empire_s']/@short_name">
        <xsl:attribute name="short_name">Hyderabad</xsl:attribute>
    </xsl:template>
    <xsl:template match="*[@id='empire_s']/@text">
        <xsl:attribute name="text">When the court in Delhi descended into hedonism and incompetence, the Nizam-ul-Mulk marched south to save what he could of the Mughal dream. Hyderabad is not a rebellion; it is the Empire purified. Here, the administration is efficient, the soldiers are disciplined, and the law is absolute. The Nizam views himself as the true custodian of Mughal heritage, ruling independently only because the Emperor is too weak to lead. The Hyderabad military machine is a blend of heavy Turko-Persian cavalry and Deccani resilience. Situated on the rugged plateau, they are the shield that protects the south from the Maratha hordes. They value order above all else, and their generals are scholar-warriors who study logistics as intently as they study the sword.</xsl:attribute>
    </xsl:template>
    <xsl:template match="*[@id='empire_s']/@title">
        <xsl:attribute name="title">Hyderabaad</xsl:attribute>
    </xsl:template>
    <xsl:template match="*[@id='sturgia']/@name">
        <xsl:attribute name="name">Daulat e Durrani</xsl:attribute>
    </xsl:template>
    <xsl:template match="*[@id='sturgia']/@ruler_title">
        <xsl:attribute name="ruler_title">Amir e Amiraan</xsl:attribute>
    </xsl:template>
    <xsl:template match="*[@id='sturgia']/@short_name">
        <xsl:attribute name="short_name">Durrani Empire</xsl:attribute>
    </xsl:template>
    <xsl:template match="*[@id='sturgia']/@text">
        <xsl:attribute name="text">From the frozen mountain passes of the north come the Afghans, a hard and ruthless people who view the fertile plains of India as a prize waiting to be taken. United under a charismatic conqueror, they have swept down to shatter the remnants of the old order. They are not interested in administration or diplomacy; they come to conquer, plunder, and establish their dominance through sheer brute force. The Afghan war machine is a terrifying wall of iron and muscle. Their infantry, the Rohillas, are famous for their unbreakable shield walls and deadly usage of long muskets, while their heavy cavalry rides horses bred for endurance in the harsh Hindu Kush. They excel in winter warfare and sieges, grinding their opponents down with a relentlessness that civilization has forgotten how to handle.</xsl:attribute>
    </xsl:template>
    <xsl:template match="*[@id='sturgia']/@title">
        <xsl:attribute name="title">Dawlat e Durrani</xsl:attribute>
    </xsl:template>
    <xsl:template match="*[@id='aserai']/@name">
        <xsl:attribute name="name">Kingdom of Mysore</xsl:attribute>
    </xsl:template>
    <xsl:template match="*[@id='aserai']/@ruler_title">
        <xsl:attribute name="ruler_title">Maharaaja Adhiraaja</xsl:attribute>
    </xsl:template>
    <xsl:template match="*[@id='aserai']/@short_name">
        <xsl:attribute name="short_name">Mysore</xsl:attribute>
    </xsl:template>
    <xsl:template match="*[@id='aserai']/@text">
        <xsl:attribute name="text">Isolated in the deep south, guarded by jungles and plateaus, the Kingdom of Mysore has grown strong while the north distracts itself with civil war. Ruled by an ancient dynasty but powered by ruthless military modernizers, Mysore is a state punching well above its weight. They are a compact, highly centralized kingdom that has embraced new technologies to hold off encroaching colonial powers and rival neighbors alike. Mysores military is unique, blending traditional southern tactics with modern innovation. They are the pioneers of rocket artillery, deploying iron-cased rockets that scream across the battlefield to shatter enemy morale. Their roster includes swift infantry suited for jungle warfare and heavy war elephants that act as mobile fortresses. They are the sleeping tiger of the south, waiting for the right moment to pounce.</xsl:attribute>
    </xsl:template>
    <xsl:template match="*[@id='aserai']/@title">
        <xsl:attribute name="title">Kingdom of Mysore</xsl:attribute>
    </xsl:template>
    <xsl:template match="*[@id='vlandia']/@name">
        <xsl:attribute name="name">The Rajputaana Federation</xsl:attribute>
    </xsl:template>
    <xsl:template match="*[@id='vlandia']/@ruler_title">
        <xsl:attribute name="ruler_title">Maharaja</xsl:attribute>
    </xsl:template>
    <xsl:template match="*[@id='vlandia']/@short_name">
        <xsl:attribute name="short_name">Rajputaana</xsl:attribute>
    </xsl:template>
    <xsl:template match="*[@id='vlandia']/@text">
        <xsl:attribute name="text">The Rajputs are the ancient aristocracy of the land, a warrior caste obsessed with lineage, honor, and the defense of their ancestral forts. For centuries, they served as the sword-arm of the Empire, but as Delhi falters, the Rajput clans have retreated to their desert strongholds to rule as independent kings. They are a feudal society where every hilltop fort houses a lord who claims descent from the sun or the moon. Their method of war is steeped in tradition. They field the finest shock cavalry in the known world, charging headlong into enemy lines seeking personal glory. Their infantry are stoic defenders of their massive stone fortifications. While individually they are the finest warriors on the continent, their pride is their undoing; a Rajput king would rather die than take orders from a rival clan.</xsl:attribute>
    </xsl:template>
    <xsl:template match="*[@id='vlandia']/@title">
        <xsl:attribute name="title">The Rajputaana Union</xsl:attribute>
    </xsl:template>
    <xsl:template match="*[@id='battania']/@name">
        <xsl:attribute name="name">Maratha Federation</xsl:attribute>
    </xsl:template>
    <xsl:template match="*[@id='battania']/@ruler_title">
        <xsl:attribute name="ruler_title">Chhatrapati</xsl:attribute>
    </xsl:template>
    <xsl:template match="*[@id='battania']/@short_name">
        <xsl:attribute name="short_name">Marathas</xsl:attribute>
    </xsl:template>
    <xsl:template match="*[@id='battania']/@text">
        <xsl:attribute name="text">Born from the rugged Sahyadri mountains, the Marathas are the masters of asymmetric warfare. What began as a peasant rebellion has evolved into a continent-spanning confederacy of warrior-chieftains. They despise the static, heavy warfare of the Mughals, preferring to strike like lightning and vanish into the forests before the enemy can form a line. To the established powers, they are bandits; to their people, they are the reclaimers of the homeland. Their military is built on speed and ambush. Deadly skirmishers and light cavalry dominate their ranks, capable of traversing the roughest terrain to outmaneuver heavily armored foes. While their loose confederacy structure often leads to internal squabbles between powerful chieftains, their collective cry of Har Har Mahadev is enough to strike fear into the hearts of the strongest empires.</xsl:attribute>
    </xsl:template>
    <xsl:template match="*[@id='battania']/@title">
        <xsl:attribute name="title">Maratha Saamrajya</xsl:attribute>
    </xsl:template>
    <xsl:template match="*[@id='khuzait']/@name">
        <xsl:attribute name="name">Sikh Confederation</xsl:attribute>
    </xsl:template>
    <xsl:template match="*[@id='khuzait']/@ruler_title">
        <xsl:attribute name="ruler_title">Sarkaar e Khaalsa</xsl:attribute>
    </xsl:template>
    <xsl:template match="*[@id='khuzait']/@short_name">
        <xsl:attribute name="short_name">Sikhs</xsl:attribute>
    </xsl:template>
    <xsl:template match="*[@id='khuzait']/@text">
        <xsl:attribute name="text">The Sikhs are a people forged in the fires of persecution. Hunted by the Mughals and Afghans alike, they have transformed their community into a mobile martial order known as the Dal Khalsa. They recognize no king but the Almighty, and their society is radically egalitarian. Organized into independent Misls or warbands, they roam the plains of the Punjab, protecting the peasantry and exacting justice on tyrants. In battle, the Sikhs are unmatched horsemen, rivaling the steppe nomads of old. They utilize hit-and-run tactics, feigning retreat to draw enemies out of formation before wheeling back to deliver a crushing volley of musket fire. They possess high morale and immense stamina, fighting not for land or coin, but for the survival of their faith and freedom.</xsl:attribute>
    </xsl:template>
    <xsl:template match="*[@id='khuzait']/@title">
        <xsl:attribute name="title">Sikh Empire</xsl:attribute>
    </xsl:template>
</xsl:stylesheet>