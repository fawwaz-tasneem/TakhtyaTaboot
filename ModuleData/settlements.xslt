<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
    <xsl:output omit-xml-declaration="yes"/>

    <xsl:template match="@*|node()">
        <xsl:copy>
            <xsl:apply-templates select="@*|node()"/>
        </xsl:copy>
    </xsl:template>

    <xsl:template match="Settlement[@id='castle_EN1']/@name">
        <xsl:attribute name="name">Fatehpur Sikri</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_EN2']/@name">
        <xsl:attribute name="name">Qila e Aligarh</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_EN3']/@name">
        <xsl:attribute name="name">Sambhal</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_EN4']/@name">
        <xsl:attribute name="name">Meerut</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_EN5']/@name">
        <xsl:attribute name="name">Kanpur</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_EN6']/@name">
        <xsl:attribute name="name">Kannauj</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_EN7']/@name">
        <xsl:attribute name="name">Etawah</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_EN8']/@name">
        <xsl:attribute name="name">Moradabad</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_EN9']/@name">
        <xsl:attribute name="name">Tughlaqabad</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_EN1']/@name">
        <xsl:attribute name="name">Akbarabad</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_EN2']/@name">
        <xsl:attribute name="name">Shahjahanabad</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_EN3']/@name">
        <xsl:attribute name="name">Allahabad</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_EN4']/@name">
        <xsl:attribute name="name">Lucknow</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_EN5']/@name">
        <xsl:attribute name="name">Mathura</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_EN6']/@name">
        <xsl:attribute name="name">Bareilly</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_EW1']/@name">
        <xsl:attribute name="name">Golconda Fort</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_EW2']/@name">
        <xsl:attribute name="name">Daulatabad</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_EW3']/@name">
        <xsl:attribute name="name">Naldurg</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_EW4']/@name">
        <xsl:attribute name="name">Udgir</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_EW5']/@name">
        <xsl:attribute name="name">Mahur</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_EW6']/@name">
        <xsl:attribute name="name">Parenda</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_EW7']/@name">
        <xsl:attribute name="name">Kaulas</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_EW8']/@name">
        <xsl:attribute name="name">Medak Fort</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_EW1']/@name">
        <xsl:attribute name="name">Warangal</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_EW2']/@name">
        <xsl:attribute name="name">Hyderabad</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_EW3']/@name">
        <xsl:attribute name="name">Aurangabad</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_EW4']/@name">
        <xsl:attribute name="name">Elichpur</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_EW5']/@name">
        <xsl:attribute name="name">Burhanpur</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_EW6']/@name">
        <xsl:attribute name="name">Bidar</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_ES1']/@name">
        <xsl:attribute name="name">Rohtasgarh</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_ES2']/@name">
        <xsl:attribute name="name">Gaur</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_ES3']/@name">
        <xsl:attribute name="name">Rajmahal</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_ES4']/@name">
        <xsl:attribute name="name">Burdwan</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_ES5']/@name">
        <xsl:attribute name="name">Sylhet</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_ES6']/@name">
        <xsl:attribute name="name">Midnapore</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_ES7']/@name">
        <xsl:attribute name="name">Shah Sarai</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_ES8']/@name">
        <xsl:attribute name="name">Purnea</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_ES1']/@name">
        <xsl:attribute name="name">Murshidabad</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_ES2']/@name">
        <xsl:attribute name="name">Dacca</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_ES3']/@name">
        <xsl:attribute name="name">Cuttack</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_ES4']/@name">
        <xsl:attribute name="name">Patna</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_ES5']/@name">
        <xsl:attribute name="name">Hooghly</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_ES6']/@name">
        <xsl:attribute name="name">Monghyr</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_ES7']/@name">
        <xsl:attribute name="name">Chittagong</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_S1']/@name">
        <xsl:attribute name="name">Bala Hissar</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_S2']/@name">
        <xsl:attribute name="name">Khyber Pass</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_S3']/@name">
        <xsl:attribute name="name">Bamyan</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_S4']/@name">
        <xsl:attribute name="name">Farah</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_S5']/@name">
        <xsl:attribute name="name">Lashkar Gah</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_S6']/@name">
        <xsl:attribute name="name">Kunduz</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_S7']/@name">
        <xsl:attribute name="name">Ghor</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_S8']/@name">
        <xsl:attribute name="name">Zaranj</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_S1']/@name">
        <xsl:attribute name="name">Kabul</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_S2']/@name">
        <xsl:attribute name="name">Ghazni</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_S3']/@name">
        <xsl:attribute name="name">Peshawar</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_S4']/@name">
        <xsl:attribute name="name">Jalalabad</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_S5']/@name">
        <xsl:attribute name="name">Balkh</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_S6']/@name">
        <xsl:attribute name="name">Herat</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_S7']/@name">
        <xsl:attribute name="name">Kandahar</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_A1']/@name">
        <xsl:attribute name="name">Devanahalli</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_A2']/@name">
        <xsl:attribute name="name">Nandi Hills</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_A3']/@name">
        <xsl:attribute name="name">Savandurga</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_A4']/@name">
        <xsl:attribute name="name">Madhugiri</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_A5']/@name">
        <xsl:attribute name="name">Dindigul</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_A6']/@name">
        <xsl:attribute name="name">Palakkad Fort</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_A7']/@name">
        <xsl:attribute name="name">Krishnagiri</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_A8']/@name">
        <xsl:attribute name="name">Bellary</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_A9']/@name">
        <xsl:attribute name="name">Gurramkonda</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_A1']/@name">
        <xsl:attribute name="name">Srirangapatna</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_A2']/@name">
        <xsl:attribute name="name">Chitradurga</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_A3']/@name">
        <xsl:attribute name="name">Bednore</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_A4']/@name">
        <xsl:attribute name="name">Coimbatore</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_A5']/@name">
        <xsl:attribute name="name">Mangalore</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_A6']/@name">
        <xsl:attribute name="name">Bangalore</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_A7']/@name">
        <xsl:attribute name="name">Mysore</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_A8']/@name">
        <xsl:attribute name="name">Sira</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_V1']/@name">
        <xsl:attribute name="name">Chittorgarh</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_V2']/@name">
        <xsl:attribute name="name">Kumbhalgarh</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_V3']/@name">
        <xsl:attribute name="name">Ranthambore</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_V4']/@name">
        <xsl:attribute name="name">Mehrangarh</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_V5']/@name">
        <xsl:attribute name="name">Junagarh</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_V6']/@name">
        <xsl:attribute name="name">Gagron</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_V7']/@name">
        <xsl:attribute name="name">Taragarh</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_V8']/@name">
        <xsl:attribute name="name">Nagaur</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_V1']/@name">
        <xsl:attribute name="name">Ajmer</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_V2']/@name">
        <xsl:attribute name="name">Udaipur</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_V3']/@name">
        <xsl:attribute name="name">Amber</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_V5']/@name">
        <xsl:attribute name="name">Gwalior</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_V6']/@name">
        <xsl:attribute name="name">Jaisalmer</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_V7']/@name">
        <xsl:attribute name="name">Bundi</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_V8']/@name">
        <xsl:attribute name="name">Bikaner</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_V9']/@name">
        <xsl:attribute name="name">Jodhpur</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_B1']/@name">
        <xsl:attribute name="name">Raigadh</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_B2']/@name">
        <xsl:attribute name="name">Pratapgadh</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_B3']/@name">
        <xsl:attribute name="name">Torna</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_B4']/@name">
        <xsl:attribute name="name">Panhala</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_B5']/@name">
        <xsl:attribute name="name">Shivneri</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_B6']/@name">
        <xsl:attribute name="name">Lohagad</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_B7']/@name">
        <xsl:attribute name="name">Vasai</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_B8']/@name">
        <xsl:attribute name="name">Solapur</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_B1']/@name">
        <xsl:attribute name="name">Pune</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_B2']/@name">
        <xsl:attribute name="name">Kolhapur</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_B3']/@name">
        <xsl:attribute name="name">Surat</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_B4']/@name">
        <xsl:attribute name="name">Satara</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_B5']/@name">
        <xsl:attribute name="name">Nashik</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_K1']/@name">
        <xsl:attribute name="name">Anandpur Sahib</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_K2']/@name">
        <xsl:attribute name="name">Gobindgarh</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_K3']/@name">
        <xsl:attribute name="name">Attock</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_K4']/@name">
        <xsl:attribute name="name">Bhatinda</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_K5']/@name">
        <xsl:attribute name="name">Kapurthala</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_K6']/@name">
        <xsl:attribute name="name">Kasur</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_K7']/@name">
        <xsl:attribute name="name">Phillaur</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_K8']/@name">
        <xsl:attribute name="name">Jamrud</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_K9']/@name">
        <xsl:attribute name="name">Jhelum</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_K1']/@name">
        <xsl:attribute name="name">Sirhind</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_K2']/@name">
        <xsl:attribute name="name">Jalandhar</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_K3']/@name">
        <xsl:attribute name="name">Lahore</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_K4']/@name">
        <xsl:attribute name="name">Multan</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_K5']/@name">
        <xsl:attribute name="name">Amritsar</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='town_K6']/@name">
        <xsl:attribute name="name">Sialkot</xsl:attribute>
    </xsl:template>
</xsl:stylesheet>